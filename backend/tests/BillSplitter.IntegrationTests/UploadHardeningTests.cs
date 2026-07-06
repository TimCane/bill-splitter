using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>
/// Upload hardening on <c>POST /sessions</c> (docs/10-security-privacy.md#upload-hardening,
/// M7 A3): magic-byte sniffing, the header-dimension decode-bomb guard, and the
/// size cap that answers 413.
/// </summary>
[Collection(SessionApiCollection.Name)]
public sealed class UploadHardeningTests(SessionApiFactory factory)
{
    // Valid JPEG header (SOI + SOF0) declaring the given canvas; trailing bytes
    // pad the body without affecting the header read.
    private static byte[] Jpeg(int width, int height, int padTo = 0)
    {
        byte[] header =
        [
            0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
        ];
        if (padTo <= header.Length)
        {
            return header;
        }

        var padded = new byte[padTo];
        header.CopyTo(padded, 0);
        return padded;
    }

    private async Task<HttpResponseMessage> PostAsync(byte[] bytes, string contentType, string fileName)
    {
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "image", fileName);
        return await client.PostAsync("/api/v1/sessions", form);
    }

    [Fact]
    public async Task A_non_image_body_is_rejected_on_its_magic_bytes_not_its_content_type()
    {
        // A PDF wearing an image content-type and a .jpg name; the sniff sees past both.
        var pdf = "%PDF-1.7\n"u8.ToArray();

        var response = await PostAsync(pdf, "image/jpeg", "receipt.jpg");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("validation");
    }

    [Fact]
    public async Task An_over_dimension_image_is_rejected_as_a_decode_bomb()
    {
        var response = await PostAsync(Jpeg(9000, 9000), "image/jpeg", "huge.jpg");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("image-too-large");
    }

    [Fact]
    public async Task An_over_size_image_is_rejected_with_413()
    {
        // Just over the 10MB cap but under the Kestrel margin: the explicit check fires.
        var oversize = Jpeg(100, 100, padTo: 10_485_760 + 200_000);

        var response = await PostAsync(oversize, "image/jpeg", "big.jpg");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("image-too-large");
    }
}
