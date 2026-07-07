namespace BillSplitter.Domain.Common;

/// <summary>
/// Stable machine-readable error codes surfaced as ProblemDetails <c>type</c>
/// (docs/04-api-contract.md#errors) and hub <c>HubException</c> messages
/// (docs/05-realtime-contract.md). The frontend switches on these strings.
/// </summary>
public static class ErrorCodes
{
    public const string Validation = "validation";
    public const string MissingToken = "missing-token";
    public const string NotHost = "not-host";
    public const string UnknownParticipant = "unknown-participant";
    public const string SessionNotFound = "session-not-found";
    public const string ItemNotFound = "item-not-found";
    public const string ReceiptNotFound = "receipt-not-found";
    public const string WrongState = "wrong-state";
    public const string SessionFull = "session-full";
    public const string ImageTooLarge = "image-too-large";
    public const string RateLimited = "rate-limited";
    public const string ConflictRetryExhausted = "conflict-retry-exhausted";
    public const string Unauthorized = "unauthorized";
}
