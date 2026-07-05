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

    [Range(0, 1024)]
    public int QueueCapacity { get; set; } = 16;
}
