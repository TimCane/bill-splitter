using System.Text.Json.Serialization;

namespace BillSplitter.Domain.Sessions;

/// <summary>Bill extras plus the printed total. <c>SubtotalMinor</c> is always
/// computed from the items, never stored (docs/02-domain-model.md#bill).</summary>
public sealed class Bill
{
    [JsonConstructor]
    public Bill(long taxMinor, long tipMinor, long serviceMinor, long totalMinor)
    {
        TaxMinor = taxMinor;
        TipMinor = tipMinor;
        ServiceMinor = serviceMinor;
        TotalMinor = totalMinor;
    }

    public long TaxMinor { get; private set; }

    public long TipMinor { get; private set; }

    public long ServiceMinor { get; private set; }

    public long TotalMinor { get; private set; }

    internal void Set(long taxMinor, long tipMinor, long serviceMinor, long totalMinor)
    {
        TaxMinor = taxMinor;
        TipMinor = tipMinor;
        ServiceMinor = serviceMinor;
        TotalMinor = totalMinor;
    }
}
