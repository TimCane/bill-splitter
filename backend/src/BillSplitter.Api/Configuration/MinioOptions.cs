using System.ComponentModel.DataAnnotations;

namespace BillSplitter.Api.Configuration;

public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    [Required]
    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "bill-splitter";
}
