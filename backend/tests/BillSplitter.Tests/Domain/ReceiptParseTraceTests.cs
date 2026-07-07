using System.Text.Json;
using BillSplitter.Domain.Parsing.Classification;
using BillSplitter.Domain.Parsing.Engine;
using BillSplitter.Domain.Parsing.Models;
using BillSplitter.Domain.Parsing.Rules;
using BillSplitter.Domain.Receipts;
using FluentAssertions;

namespace BillSplitter.Tests.Domain;

/// <summary>
/// Asserts the in-memory parse-decision trace (docs/15-receipt-parsing.md#diagnostics):
/// which rule won a line and at what score. The trace is a test-only surface on the
/// internal engine (<see cref="ReceiptParseEngine.ParseTraced"/>), never on the
/// public <see cref="ParsedReceipt"/> and never logged. It reuses the fixture corpus
/// so the receipts asserted here are the same ones
/// <see cref="ReceiptParserCorpusTests"/> keeps green.
/// </summary>
public sealed class ReceiptParseTraceTests
{
    private static readonly string CorpusRoot =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "receipts");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static IReadOnlyList<ParseDecision> TraceFor(string fixture)
    {
        var ocr = JsonSerializer.Deserialize<OcrResult>(
            File.ReadAllText(Path.Combine(CorpusRoot, fixture, "ocr.json")), Json)!;
        return ReceiptParseEngine.ParseTraced(ocr).Trace;
    }

    [Fact]
    public void Traces_the_unit_price_column_rule_that_won_an_item_row()
    {
        // "2 Roast Beef 27.00 54.00 B": the per-unit column reconciles (27.00 x 2),
        // so UnitPriceColumnRule outscores the default name reading.
        var beef = TraceFor("pig-and-butcher-tax-codes").Single(d => d.Text.Contains("Roast Beef"));

        beef.Type.Should().Be(LineType.Item);
        beef.Rule.Should().Be(nameof(UnitPriceColumnRule));
        beef.Score.Should().Be(UnitPriceColumnRule.Confidence);
    }

    [Fact]
    public void Traces_the_default_name_reading_for_a_plain_item()
    {
        // "3 CROISSANT 9.00": no per-unit column to drop, so the always-applies
        // QuantityNamePriceRule wins at its lower confidence.
        var croissant = TraceFor("quantity-lines").Single(d => d.Text.Contains("CROISSANT"));

        croissant.Type.Should().Be(LineType.Item);
        croissant.Rule.Should().Be(nameof(QuantityNamePriceRule));
        croissant.Score.Should().Be(QuantityNamePriceRule.Confidence);
    }

    [Fact]
    public void Traces_the_grand_total_anchor()
    {
        var total = TraceFor("quantity-lines")
            .Single(d => d.Type == LineType.Total && d.Rule == "GrandTotalDetector");

        total.Text.Should().Contain("TOTAL");
    }

    [Fact]
    public void Traces_a_bill_extra_by_keyword()
    {
        // us-tax-tip carries an itemised TAX and TIP the keyword classifier reads.
        var trace = TraceFor("us-tax-tip");

        trace.Should().Contain(d => d.Type == LineType.Tax && d.Rule == nameof(KeywordClassifier));
        trace.Should().Contain(d => d.Type == LineType.Tip && d.Rule == nameof(KeywordClassifier));
    }

    [Fact]
    public void Trace_is_absent_from_the_public_parse_result()
    {
        // The wire-facing ParsedReceipt exposes no trace; the diagnostic lives only
        // on the internal ParseTraced. Guards the no-PII rule - nothing about a
        // receipt's lines can ride the public result into a log.
        typeof(ParsedReceipt).GetProperty("Trace").Should().BeNull();
    }
}
