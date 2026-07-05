namespace BillSplitter.Domain;

/// <summary>
/// Thrown by aggregate mutations when a domain rule is violated. <see cref="Code"/>
/// is one of <see cref="ErrorCodes"/>; the API middleware maps it to an HTTP status
/// and a ProblemDetails <c>type</c> (docs/04-api-contract.md#errors).
/// </summary>
public sealed class DomainException(string code, string? detail = null)
    : Exception(detail ?? code)
{
    public string Code { get; } = code;

    public string? Detail { get; } = detail;
}
