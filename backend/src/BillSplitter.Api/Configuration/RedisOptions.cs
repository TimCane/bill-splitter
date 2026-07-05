using System.ComponentModel.DataAnnotations;

namespace BillSplitter.Api.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
