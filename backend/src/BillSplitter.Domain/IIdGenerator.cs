namespace BillSplitter.Domain;

/// <summary>
/// Cryptographically-random identifiers. Ids are 22-char base64url (16 bytes),
/// tokens 43-char base64url (32 bytes), short codes 6 chars from the ambiguity-free
/// alphabet (docs/02-domain-model.md#entities, docs/07-backend-design.md).
/// </summary>
public interface IIdGenerator
{
    /// <summary>A 22-char base64url id for a session, participant or item.</summary>
    string NewId();

    /// <summary>A 43-char base64url participant token (returned once, never stored raw).</summary>
    string NewToken();

    /// <summary>A 6-char short code from the no-0/O/1/I/L alphabet.</summary>
    string NewShortCode();
}
