using System.Globalization;
using System.Text.RegularExpressions;

namespace BillSplitter.Domain;

/// <summary>
/// Pure OCR-lines-to-<see cref="ParsedReceipt"/> heuristics
/// (docs/06-ocr-service.md#parsing). Deterministic on the sidecar's JSON, so the
/// fixture corpus is its real spec (docs/11-testing-strategy.md#receiptparser).
/// Every rule here is expected to grow with that corpus.
/// </summary>
public static partial class ReceiptParser
{
    /// <summary>Lines the sidecar recognized below this land in <c>Warnings</c>
    /// rather than as items - too unreliable to seed the split from.</summary>
    private const double ConfidenceFloor = 0.5;

    // A money token at the end of the line: 1-4 whole digits, a '.' or ',', two
    // fraction digits, optionally preceded by a currency symbol or a minus. The
    // end anchor is the reject for "11.00%" - trailing text means it is not a
    // clean amount row.
    [GeneratedRegex(@"(?<neg>-\s*)?(?<sym>[£€$])?\s*(?<whole>\d{1,4})[.,](?<frac>\d{2})\s*$")]
    private static partial Regex MoneyAtEnd();

    // A leading quantity: "2 ", "2x ", "3 X " -> the count, stripped from the name.
    [GeneratedRegex(@"^(?<qty>\d{1,2})\s?[xX]?\s+")]
    private static partial Regex LeadingQuantity();

    // A "#12"-style item code, dropped from the name.
    [GeneratedRegex(@"#\S+")]
    private static partial Regex ItemCode();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static ParsedReceipt Parse(OcrResult result)
    {
        var warnings = new List<string>();
        var currency = GuessCurrency(result.Lines);

        var candidates = new List<Candidate>();
        foreach (var line in result.Lines)
        {
            var text = (line.Text ?? string.Empty).Trim();
            var money = MoneyAtEnd().Match(text);
            if (!money.Success)
            {
                // No amount: a header, address or free-text line. Ignored, not a warning.
                continue;
            }

            if (line.Confidence < ConfidenceFloor)
            {
                warnings.Add($"low-confidence line ignored: {text}");
                continue;
            }

            if (money.Groups["neg"].Success)
            {
                // Discounts print negative; MVP parks them for the host, unmodeled.
                warnings.Add($"negative amount ignored: {text}");
                continue;
            }

            var name = text[..money.Index].Trim();
            candidates.Add(new Candidate(line.Box.Y, ToMinor(money), name));
        }

        long tax = 0, tip = 0, service = 0;
        Candidate? total = null;
        var itemRows = new List<Candidate>();

        foreach (var candidate in candidates)
        {
            var upper = candidate.Name.ToUpperInvariant();
            if (IsSubtotal(upper))
            {
                continue; // We compute our own subtotal.
            }

            if (IsTax(upper))
            {
                tax = candidate.Amount;
            }
            else if (IsTip(upper))
            {
                tip = candidate.Amount;
            }
            else if (IsService(upper))
            {
                service = candidate.Amount;
            }
            else if (IsTotal(upper))
            {
                // Lowest on the receipt wins (grand totals print last); a
                // same-row tie takes the larger amount.
                if (total is null
                    || candidate.Y > total.Y
                    || (candidate.Y == total.Y && candidate.Amount > total.Amount))
                {
                    total = candidate;
                }
            }
            else if (!IsPaymentNoise(upper))
            {
                itemRows.Add(candidate);
            }
        }

        var totalY = total?.Y ?? int.MaxValue;
        var items = new List<ParsedItem>();
        foreach (var row in itemRows)
        {
            if (row.Y >= totalY)
            {
                // Below the grand total: trailing noise, not an ordered item.
                continue;
            }

            var (quantity, name) = ExtractQuantity(CleanName(row.Name));
            if (name.Length == 0)
            {
                warnings.Add($"price with no name ignored: {row.Amount}");
                continue;
            }

            items.Add(new ParsedItem(name, quantity, row.Amount));
        }

        var bill = new Bill(tax, tip, service, total?.Amount ?? 0);
        return new ParsedReceipt(items, bill, currency, warnings);
    }

    private static long ToMinor(Match money) =>
        (long.Parse(money.Groups["whole"].Value, CultureInfo.InvariantCulture) * 100)
        + long.Parse(money.Groups["frac"].Value, CultureInfo.InvariantCulture);

    private static string GuessCurrency(IReadOnlyList<OcrLine> lines)
    {
        foreach (var line in lines)
        {
            foreach (var ch in line.Text ?? string.Empty)
            {
                switch (ch)
                {
                    case '£': return "GBP";
                    case '€': return "EUR";
                    case '$': return "USD";
                }
            }
        }

        return Session.DefaultCurrency;
    }

    private static string CleanName(string raw)
    {
        var name = ItemCode().Replace(raw, string.Empty);
        name = name.Trim().TrimEnd('.', ' ', '-'); // strip dot leaders
        return Whitespace().Replace(name, " ").Trim();
    }

    private static (int Quantity, string Name) ExtractQuantity(string name)
    {
        var match = LeadingQuantity().Match(name);
        if (!match.Success)
        {
            return (1, name);
        }

        var rest = name[match.Length..].Trim();
        var quantity = int.Parse(match.Groups["qty"].Value, CultureInfo.InvariantCulture);
        return rest.Length > 0 && quantity >= 1 ? (quantity, rest) : (1, name);
    }

    private static bool IsSubtotal(string u) => u.Contains("SUBTOTAL") || u.Contains("SUB TOTAL");

    private static bool IsTax(string u) => u.Contains("TAX") || u.Contains("VAT") || u.Contains("GST");

    private static bool IsTip(string u) => u.Contains("TIP") || u.Contains("GRATUITY");

    private static bool IsService(string u) => u.Contains("SERVICE");

    private static bool IsTotal(string u) =>
        u.Contains("TOTAL") || u.Contains("AMOUNT DUE") || u.Contains("BALANCE DUE") || u.Contains("TO PAY");

    private static bool IsPaymentNoise(string u) =>
        u.Contains("CASH") || u.Contains("CHANGE") || u.Contains("CARD")
        || u.Contains("VISA") || u.Contains("MASTERCARD") || u.Contains("AUTH");

    private sealed record Candidate(int Y, long Amount, string Name);
}
