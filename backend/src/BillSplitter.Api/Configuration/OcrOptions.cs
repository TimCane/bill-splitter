using System.ComponentModel.DataAnnotations;

namespace BillSplitter.Api.Configuration;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 60;

    [Range(1, 16)]
    public int MaxConcurrency { get; set; } = 2;

    // Lower bound is 1: Channel.CreateBounded rejects a capacity of 0.
    [Range(1, 1024)]
    public int QueueCapacity { get; set; } = 16;
}
