using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BillSplitter.Domain.Abstractions;
using BillSplitter.Domain.Receipts;

namespace BillSplitter.Infrastructure.Ocr;

/// <summary>
/// Typed <see cref="IOcrClient"/> over the sidecar's <c>POST /ocr</c>. Registered
/// as a typed <see cref="HttpClient"/> with the base URL and 60s timeout from
/// <c>OcrOptions</c> (docs/07-backend-design.md#infrastructure-project). Non-2xx
/// responses and connection failures surface as exceptions; the worker decides
/// what to retry (docs/06-ocr-service.md#backend-job-flow).
/// </summary>
public sealed class HttpOcrClient(HttpClient http) : IOcrClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct)
    {
        using var content = new StreamContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var response = await http.PostAsync("/ocr", content, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OcrResult>(Json, ct)
            ?? throw new InvalidOperationException("OCR sidecar returned an empty body");
    }
}
