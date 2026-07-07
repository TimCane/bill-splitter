namespace BillSplitter.Domain.Sessions;

/// <summary>Lifecycle of a session (docs/02-domain-model.md).</summary>
public enum SessionState
{
    Processing,
    Review,
    Open,
    Finalized,
}
