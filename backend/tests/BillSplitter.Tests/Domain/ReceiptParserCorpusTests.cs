using System.Text.Json;
using BillSplitter.Domain;
using FluentAssertions;

namespace BillSplitter.Tests.Domain;

/// <summary>
/// Runs the whole fixture corpus through <see cref="ReceiptParser"/>: each
/// receipt is a recorded sidecar response (<c>ocr.json</c>) plus the expected
/// parse (<c>expected.json</c>). This corpus is the parser's real spec - grow it
/// from real receipts, adding a fixture for every misparse before fixing it
/// (docs/11-testing-strategy.md#receiptparser).
/// </summary>
public sealed class ReceiptParserCorpusTests
{
    private static readonly string CorpusRoot =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "receipts");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.GetDirectories(CorpusRoot))
        {
            data.Add(Path.GetFileName(dir));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Parses_to_the_expected_receipt(string fixture)
    {
        var dir = Path.Combine(CorpusRoot, fixture);
        var ocr = JsonSerializer.Deserialize<OcrResult>(File.ReadAllText(Path.Combine(dir, "ocr.json")), Json)!;
        var expected = JsonSerializer.Deserialize<ParsedReceipt>(
            File.ReadAllText(Path.Combine(dir, "expected.json")), Json)!;

        var parsed = ReceiptParser.Parse(ocr);

        parsed.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Keeps_the_named_seed_fixtures()
    {
        // The seed set from docs/14 must always be present; real-receipt fixtures
        // are added alongside it, so assert the seeds rather than a fixed count.
        var names = Directory.GetDirectories(CorpusRoot).Select(Path.GetFileName);

        names.Should().Contain(new[]
        {
            "clean-uk", "us-tax-tip", "quantity-lines", "service-charge",
            "dot-leaders", "blurry-low-confidence", "non-receipt",
        });
    }
}
