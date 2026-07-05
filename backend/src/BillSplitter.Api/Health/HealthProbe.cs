using BillSplitter.Api.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BillSplitter.Api.Health;

/// <summary>Result of a /healthz sweep. <see cref="Email"/> is a capability
/// flag, not a probe, and never affects <see cref="IsHealthy"/>.</summary>
public sealed record HealthReport(bool Redis, bool Minio, bool Ocr, bool Email)
{
    public bool IsHealthy => Redis && Minio && Ocr;
}

/// <summary>Probes the three backing services in parallel. Any failure is a
/// <c>false</c> flag, never an exception - /healthz must always answer.</summary>
public sealed class HealthProbe
{
    public const string HttpClientName = "health";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly IConnectionMultiplexer _redis;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MinioOptions _minio;
    private readonly OcrOptions _ocr;
    private readonly bool _emailEnabled;

    public HealthProbe(
        IConnectionMultiplexer redis,
        IHttpClientFactory httpClientFactory,
        IOptions<MinioOptions> minio,
        IOptions<OcrOptions> ocr,
        IOptions<SmtpOptions> smtp)
    {
        _redis = redis;
        _httpClientFactory = httpClientFactory;
        _minio = minio.Value;
        _ocr = ocr.Value;
        _emailEnabled = smtp.Value.IsEnabled;
    }

    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        var redis = ProbeRedisAsync(ct);
        var minio = ProbeHttpAsync($"{_minio.Endpoint.TrimEnd('/')}/minio/health/live", ct);
        var ocr = ProbeHttpAsync($"{_ocr.BaseUrl.TrimEnd('/')}/healthz", ct);

        await Task.WhenAll(redis, minio, ocr);

        return new HealthReport(redis.Result, minio.Result, ocr.Result, _emailEnabled);
    }

    private async Task<bool> ProbeRedisAsync(CancellationToken ct)
    {
        try
        {
            if (!_redis.IsConnected)
            {
                return false;
            }

            // PingAsync has no token; bound it to ProbeTimeout so a stalled but
            // reachable Redis cannot push the whole sweep past its budget.
            await _redis.GetDatabase().PingAsync().WaitAsync(ProbeTimeout, ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> ProbeHttpAsync(string url, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
