using System.Buffers.Binary;

namespace BillSplitter.Api.Http;

/// <summary>
/// Reads pixel dimensions straight from the JPEG/PNG header - never decodes the
/// pixels. The decode-bomb guard uses this to reject an oversized image before it
/// reaches the OCR sidecar (docs/10-security-privacy.md#upload-hardening). Assumes
/// the bytes already sniffed as JPEG or PNG (<see cref="ImageSniffer"/>).
/// </summary>
public static class ImageDimensions
{
    /// <summary>True and the header dimensions when they can be read; false when
    /// the header is truncated or malformed (the caller treats that as invalid).</summary>
    public static bool TryRead(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (content.Length >= 8 && content[0] == 0x89 && content[1] == 0x50)
        {
            return TryReadPng(content, out width, out height);
        }

        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xD8)
        {
            return TryReadJpeg(content, out width, out height);
        }

        return false;
    }

    // PNG: the 8-byte signature is followed immediately by the IHDR chunk, whose
    // data begins with width then height as big-endian uint32 (offsets 16, 20).
    private static bool TryReadPng(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (content.Length < 24)
        {
            return false;
        }

        width = (int)BinaryPrimitives.ReadUInt32BigEndian(content.Slice(16, 4));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4));
        return width > 0 && height > 0;
    }

    // JPEG: walk the marker segments from just past SOI until a Start-Of-Frame
    // marker, which carries height then width. Every other segment is skipped by
    // its own big-endian length. No entropy-coded scan is ever entered.
    private static bool TryReadJpeg(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;

        var offset = 2;
        while (offset + 4 <= content.Length)
        {
            if (content[offset] != 0xFF)
            {
                return false;
            }

            var marker = content[offset + 1];

            // A fill byte is one 0xFF in a run; skip a single byte and re-read the
            // real marker on the next pass.
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            // Standalone 2-byte markers (TEM, RSTn, SOI/EOI) carry no length.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
            {
                offset += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 2, 2));
            if (length < 2)
            {
                return false;
            }

            // SOF0..SOF15 hold the frame size, minus the four non-dimension SOF
            // slots (DHT/JPG/DAC arithmetic-coding markers share the C-range).
            if (marker is (>= 0xC0 and <= 0xCF) and not (0xC4 or 0xC8 or 0xCC))
            {
                if (offset + 9 > content.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 7, 2));
                return width > 0 && height > 0;
            }

            offset += 2 + length;
        }

        return false;
    }
}
