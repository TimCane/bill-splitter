using BillSplitter.Api.Configuration;
using BillSplitter.Domain;
using Microsoft.Extensions.Options;

namespace BillSplitter.Api.Dtos;

/// <summary>
/// The only place a <see cref="Session"/> becomes a <see cref="SessionSnapshotDto"/>.
/// Computes every derived field (subtotal, checksum, per-participant totals via
/// <see cref="SplitCalculator"/>, expiresAt from the key's TTL) and never leaks a
/// token hash (docs/07-backend-design.md#api-project).
/// </summary>
public sealed class SnapshotMapper
{
    private readonly TimeProvider _clock;
    private readonly string _publicBaseUrl;

    public SnapshotMapper(TimeProvider clock, IOptions<AppOptions> app)
    {
        _clock = clock;
        _publicBaseUrl = app.Value.PublicBaseUrl.TrimEnd('/');
    }

    public SessionSnapshotDto Map(Session session, TimeSpan ttl)
    {
        var split = SplitCalculator.Compute(session);
        var ordered = session.Participants
            .OrderBy(p => p.JoinedAt)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        var subtotal = session.Items.Sum(i => i.PriceMinor);
        var checksum = subtotal + session.Bill.TaxMinor + session.Bill.TipMinor
            + session.Bill.ServiceMinor - session.Bill.TotalMinor;

        var participants = ordered
            .Select(p => new ParticipantDto(p.Id, p.DisplayName, session.IsHost(p.Id)))
            .ToList();

        var items = session.Items.Select(item => new ItemDto(
            item.Id,
            item.Name,
            item.Quantity,
            item.PriceMinor,
            item.Claims.Select(c => new ClaimDto(
                c.ParticipantId,
                c.Shares,
                split.Allocations.GetValueOrDefault((item.Id, c.ParticipantId)))).ToList())).ToList();

        var totals = ordered.Select(p =>
        {
            var t = split.Totals[p.Id];
            return new ParticipantTotalDto(
                p.Id, t.ItemsMinor, t.TaxMinor, t.TipMinor, t.ServiceMinor, t.UnclaimedMinor, t.TotalMinor);
        }).ToList();

        return new SessionSnapshotDto(
            SessionId: session.Id,
            Version: session.Version,
            State: session.State.ToString(),
            Currency: session.Currency,
            ExpiresAt: _clock.GetUtcNow() + ttl,
            ShortCode: session.ShortCode,
            JoinUrl: session.ShortCode is null ? null : $"{_publicBaseUrl}/s/{session.Id}",
            HostParticipantId: session.HostParticipantId,
            Ocr: new OcrDto(session.Ocr.Status.ToString(), session.Ocr.FailureReason),
            Participants: participants,
            Items: items,
            Bill: new BillDto(
                subtotal,
                session.Bill.TaxMinor,
                session.Bill.TipMinor,
                session.Bill.ServiceMinor,
                session.Bill.TotalMinor,
                checksum),
            UnclaimedTotalMinor: split.UnclaimedTotalMinor,
            Totals: totals);
    }
}
