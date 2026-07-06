namespace BillSplitter.Api.Http;

/// <summary>Identifies JPEG/PNG by magic bytes, not file extension
/// (docs/04-api-contract.md#post-apiv1sessions). Decode-bomb and full upload
/// hardening land in M7.</summary>
public static class ImageSniffer
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The image content type, or null when the bytes are neither JPEG nor PNG.</summary>
    public static string? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(Jpeg))
        {
            return "image/jpeg";
        }

        if (content.StartsWith(Png))
        {
            return "image/png";
        }

        return null;
    }
}
