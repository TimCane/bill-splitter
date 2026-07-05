using System.Security.Cryptography;
using System.Text;

namespace BillSplitter.Domain;

/// <summary>Hashes participant tokens for storage and lookup. Only the hex
/// SHA-256 is ever persisted; the raw token is returned once at create/join and
/// matched by hash thereafter (docs/04-api-contract.md#auth).</summary>
public static class TokenHasher
{
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
