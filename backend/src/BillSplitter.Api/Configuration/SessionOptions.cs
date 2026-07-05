using System.ComponentModel.DataAnnotations;

namespace BillSplitter.Api.Configuration;

public sealed class SessionOptions
{
    public const string SectionName = "Session";

    [Range(1, 168)]
    public int TtlHours { get; set; } = 24;

    [Range(1, 1440)]
    public int FinalizedTtlMinutes { get; set; } = 60;

    [Range(1, 100)]
    public int MaxParticipants { get; set; } = 20;

    [Range(1, 1000)]
    public int MaxItems { get; set; } = 100;

    [Range(1, 104857600)]
    public long MaxUploadBytes { get; set; } = 10485760;
}
