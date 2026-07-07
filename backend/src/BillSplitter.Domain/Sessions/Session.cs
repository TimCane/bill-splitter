using System.Text.Json.Serialization;
using BillSplitter.Domain.Common;
using BillSplitter.Domain.Receipts;

namespace BillSplitter.Domain.Sessions;

/// <summary>
/// The session aggregate root and the only place domain rules live. Every
/// mutation is a method here that throws <see cref="DomainException"/> on a rule
/// violation before anything is written (docs/02-domain-model.md,
/// docs/07-backend-design.md#the-mutate-pattern). Serializes to the Redis
/// document verbatim (docs/03-redis-schema.md).
/// </summary>
public sealed class Session
{
    public const string DefaultCurrency = "GBP";

    /// <summary>Upper bound on any single money field, in minor units
    /// (1,000,000.00 major). Bounding every amount keeps the amount * weight
    /// products in <see cref="SplitCalculator"/> well inside <see cref="long"/>
    /// range, so totals can never silently overflow (docs/02-domain-model.md).</summary>
    public const long MaxAmountMinor = 100_000_000;

    private readonly List<Participant> _participants;
    private readonly List<LineItem> _items;

    [JsonConstructor]
    public Session(
        string id,
        int version,
        SessionState state,
        string currency,
        string? shortCode,
        DateTimeOffset createdAt,
        DateTimeOffset? finalizedAt,
        string hostParticipantId,
        IReadOnlyList<Participant>? participants,
        IReadOnlyList<LineItem>? items,
        Bill bill,
        OcrInfo ocr)
    {
        Id = id;
        Version = version;
        State = state;
        Currency = currency;
        ShortCode = shortCode;
        CreatedAt = createdAt;
        FinalizedAt = finalizedAt;
        HostParticipantId = hostParticipantId;
        _participants = participants is null ? [] : [.. participants];
        _items = items is null ? [] : [.. items];
        Bill = bill;
        Ocr = ocr;
    }

    public string Id { get; private set; }

    /// <summary>Optimistic-concurrency counter; +1 per committed write.</summary>
    public int Version { get; private set; }

    public SessionState State { get; private set; }

    public string Currency { get; private set; }

    public string? ShortCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? FinalizedAt { get; private set; }

    public string HostParticipantId { get; private set; }

    public IReadOnlyList<Participant> Participants => _participants;

    public IReadOnlyList<LineItem> Items => _items;

    public Bill Bill { get; private set; }

    public OcrInfo Ocr { get; private set; }

    /// <summary>New session: host is participant "Host", state <c>Processing</c>,
    /// OCR <c>Pending</c>. The raw host token is hashed by the caller.</summary>
    public static Session Create(
        string id,
        string hostParticipantId,
        string hostTokenHash,
        DateTimeOffset createdAt)
    {
        var host = new Participant(hostParticipantId, hostTokenHash, "Host", createdAt);
        return new Session(
            id,
            version: 0,
            state: SessionState.Processing,
            currency: DefaultCurrency,
            shortCode: null,
            createdAt: createdAt,
            finalizedAt: null,
            hostParticipantId: hostParticipantId,
            participants: [host],
            items: [],
            bill: new Bill(0, 0, 0, 0),
            ocr: new OcrInfo(OcrStatus.Pending, null));
    }

    /// <summary>Bumped by the store immediately before the CAS commit
    /// (docs/03-redis-schema.md#concurrency).</summary>
    public void IncrementVersion() => Version++;

    // --- OCR transitions (only while Processing) ---------------------------

    public void MarkOcrProcessing()
    {
        if (State != SessionState.Processing)
        {
            return;
        }

        Ocr.Set(OcrStatus.Processing, null);
    }

    /// <summary>Apply a parse result and advance to <c>Review</c>. In M2 this is
    /// called with an empty result to fake OCR as instant-empty-Review
    /// (docs/14-build-order.md#m2---session-core).</summary>
    public void CompleteOcr(
        IEnumerable<LineItem> items, Bill bill, string currency, IEnumerable<string>? warnings = null)
    {
        if (State != SessionState.Processing)
        {
            return;
        }

        _items.Clear();
        _items.AddRange(items);
        Bill = bill;
        Currency = currency;
        Ocr.Set(OcrStatus.Done, null);
        Ocr.SetWarnings(warnings ?? []);
        State = SessionState.Review;
    }

    public void FailOcr(string reason)
    {
        if (State != SessionState.Processing)
        {
            return;
        }

        Ocr.Set(OcrStatus.Failed, reason);
        State = SessionState.Review;
    }

    // --- Participants ------------------------------------------------------

    public Participant Join(
        string participantId,
        string tokenHash,
        string displayName,
        DateTimeOffset joinedAt,
        int maxParticipants)
    {
        EnsureState(SessionState.Open, "join");
        if (_participants.Count >= maxParticipants)
        {
            throw new DomainException(ErrorCodes.SessionFull);
        }

        var participant = new Participant(participantId, tokenHash, NormalizeDisplayName(displayName), joinedAt);
        _participants.Add(participant);
        return participant;
    }

    /// <summary>Set a participant's own display name. Allowed in <c>Review</c>
    /// (host fixing their name) and <c>Open</c>.</summary>
    public void RenameParticipant(string participantId, string displayName)
    {
        if (State is not (SessionState.Review or SessionState.Open))
        {
            throw new DomainException(
                ErrorCodes.WrongState,
                $"rename requires state Review or Open but session is {State}");
        }

        var participant = FindParticipant(participantId);
        participant.Rename(NormalizeDisplayName(displayName));
    }

    // --- Item CRUD (Review only) -------------------------------------------

    public LineItem AddItem(string itemId, string name, int quantity, long priceMinor, int maxItems)
    {
        EnsureState(SessionState.Review, "add item");
        if (_items.Count >= maxItems)
        {
            throw new DomainException(ErrorCodes.Validation, $"item cap of {maxItems} reached");
        }

        var item = new LineItem(itemId, NormalizeItemName(name), ValidateQuantity(quantity), ValidatePrice(priceMinor), null);
        _items.Add(item);
        return item;
    }

    public void UpdateItem(string itemId, string name, int quantity, long priceMinor)
    {
        EnsureState(SessionState.Review, "update item");
        FindItem(itemId).Update(NormalizeItemName(name), ValidateQuantity(quantity), ValidatePrice(priceMinor));
    }

    public void RemoveItem(string itemId)
    {
        EnsureState(SessionState.Review, "remove item");
        var removed = _items.RemoveAll(i => i.Id == itemId);
        if (removed == 0)
        {
            throw new DomainException(ErrorCodes.ItemNotFound, itemId);
        }
    }

    public void SetBill(long taxMinor, long tipMinor, long serviceMinor, long totalMinor, string currency)
    {
        EnsureState(SessionState.Review, "edit bill");
        Bill.Set(
            ValidatePrice(taxMinor),
            ValidatePrice(tipMinor),
            ValidatePrice(serviceMinor),
            ValidatePrice(totalMinor));
        Currency = ValidateCurrency(currency);
    }

    // --- Claims (Open only) ------------------------------------------------

    public void ClaimItem(string itemId, string participantId) => SetShares(itemId, participantId, 1);

    public void SetShares(string itemId, string participantId, int shares)
    {
        EnsureState(SessionState.Open, "claim");
        if (shares is < 1 or > 99)
        {
            throw new DomainException(ErrorCodes.Validation, "shares must be between 1 and 99");
        }

        FindItem(itemId).SetShares(participantId, shares);
    }

    public void UnclaimItem(string itemId, string participantId)
    {
        EnsureState(SessionState.Open, "unclaim");
        FindItem(itemId).Unclaim(participantId);
    }

    // --- Lifecycle: open / finalize (host only) ----------------------------

    public void Open(string actingParticipantId, string shortCode)
    {
        EnsureState(SessionState.Review, "open");
        EnsureHost(actingParticipantId);
        ShortCode = shortCode;
        State = SessionState.Open;
        // Warnings and the failure reason are host-only Review aids; joining
        // participants read the same snapshot, so drop them at the gate.
        Ocr.ClearHostOnlyDetail();
    }

    public void Finalize(string actingParticipantId, DateTimeOffset finalizedAt)
    {
        EnsureState(SessionState.Open, "finalize");
        EnsureHost(actingParticipantId);
        FinalizedAt = finalizedAt;
        State = SessionState.Finalized;
    }

    // --- Lookups -----------------------------------------------------------

    public bool IsHost(string participantId) => participantId == HostParticipantId;

    public Participant? TryGetParticipant(string participantId) =>
        _participants.Find(p => p.Id == participantId);

    /// <summary>Match a request's token (already hashed) to a participant.</summary>
    public Participant? FindByTokenHash(string tokenHash) =>
        _participants.Find(p => p.TokenHash == tokenHash);

    private Participant FindParticipant(string participantId) =>
        TryGetParticipant(participantId)
        ?? throw new DomainException(ErrorCodes.UnknownParticipant, participantId);

    private LineItem FindItem(string itemId) =>
        _items.Find(i => i.Id == itemId)
        ?? throw new DomainException(ErrorCodes.ItemNotFound, itemId);

    // --- Guards / validation ----------------------------------------------

    private void EnsureState(SessionState required, string operation)
    {
        if (State != required)
        {
            throw new DomainException(
                ErrorCodes.WrongState,
                $"{operation} requires state {required} but session is {State}");
        }
    }

    private void EnsureHost(string participantId)
    {
        if (!IsHost(participantId))
        {
            throw new DomainException(ErrorCodes.NotHost);
        }
    }

    private static string NormalizeDisplayName(string displayName)
    {
        var trimmed = (displayName ?? string.Empty).Trim();
        if (trimmed.Length is < 1 or > 30)
        {
            throw new DomainException(ErrorCodes.Validation, "displayName must be 1-30 characters");
        }

        return trimmed;
    }

    private static string NormalizeItemName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < 1 or > 80)
        {
            throw new DomainException(ErrorCodes.Validation, "name must be 1-80 characters");
        }

        return trimmed;
    }

    private static int ValidateQuantity(int quantity)
    {
        if (quantity < 1)
        {
            throw new DomainException(ErrorCodes.Validation, "quantity must be >= 1");
        }

        return quantity;
    }

    private static long ValidatePrice(long amountMinor)
    {
        if (amountMinor is < 0 or > MaxAmountMinor)
        {
            throw new DomainException(
                ErrorCodes.Validation, $"amount must be between 0 and {MaxAmountMinor} minor units");
        }

        return amountMinor;
    }

    private static string ValidateCurrency(string currency)
    {
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException(ErrorCodes.Validation, "currency must be a 3-letter ISO 4217 code");
        }

        if (!CurrencyCodes.IsKnown(currency))
        {
            throw new DomainException(ErrorCodes.Validation, $"'{currency}' is not a known ISO 4217 currency");
        }

        return currency;
    }
}
