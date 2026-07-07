using System.Globalization;

namespace BillSplitter.Domain.Parsing;

/// <summary>Builds integer minor units from a money token's whole and fraction
/// digits (the repo's hard rule: every amount is integer minor units, never a
/// decimal). Shared by the engine's line-total parse and the item rules'
/// unit-price column so the two cannot drift on magnitude.</summary>
internal static class AmountMath
{
    public static long ToMinorUnits(string whole, string fraction) =>
        (long.Parse(whole, CultureInfo.InvariantCulture) * 100)
        + long.Parse(fraction, CultureInfo.InvariantCulture);
}
