using System.ComponentModel.DataAnnotations;

namespace BillSplitter.Api.Configuration;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Public origin used to build short-code join URLs.</summary>
    [Required]
    [Url]
    public string PublicBaseUrl { get; set; } = string.Empty;
}
