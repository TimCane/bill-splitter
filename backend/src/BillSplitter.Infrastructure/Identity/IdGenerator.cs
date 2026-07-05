using System.Buffers.Text;
using System.Security.Cryptography;
using BillSplitter.Domain;

namespace BillSplitter.Infrastructure.Identity;

/// <summary>Crypto-RNG <see cref="IIdGenerator"/> (docs/02-domain-model.md#entities).</summary>
public sealed class IdGenerator : IIdGenerator
{
    // No 0/O, 1/I/L - unambiguous when typed or read aloud.
    private const string ShortCodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string NewId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    public string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public string NewShortCode()
    {
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = ShortCodeAlphabet[RandomNumberGenerator.GetInt32(ShortCodeAlphabet.Length)];
        }

        return new string(chars);
    }
}
