using System.Globalization;

namespace BillSplitter.Domain;

/// <summary>
/// The set of active ISO 4217 currency codes a session may use. Derived from the
/// runtime's specific cultures via <see cref="RegionInfo.ISOCurrencySymbol"/>, with a
/// guaranteed seed so validation still works under invariant globalization
/// (docs/02-domain-model.md#money).
/// </summary>
public static class CurrencyCodes
{
    private static readonly HashSet<string> Known = Build();

    public static bool IsKnown(string code) => Known.Contains(code);

    private static HashSet<string> Build()
    {
        // Seed the common codes so an invariant-globalization host (where
        // GetCultures returns only the invariant culture) still validates them.
        var set = new HashSet<string>(StringComparer.Ordinal)
        {
            "GBP", "USD", "EUR", "JPY", "AUD", "CAD", "CHF", "CNY", "INR",
            "NZD", "SEK", "NOK", "DKK", "PLN", "ZAR", "HKD", "SGD", "MXN", "BRL",
        };

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                set.Add(new RegionInfo(culture.Name).ISOCurrencySymbol);
            }
            catch (ArgumentException)
            {
                // Some synthetic cultures have no region; skip them.
            }
        }

        return set;
    }
}
