using BillSplitter.Domain.Parsing.Validators;
using BillSplitter.Domain.Receipts;
using BillSplitter.Domain.Sessions;
using FluentAssertions;

namespace BillSplitter.Tests.Domain;

public sealed class TotalsValidatorTests
{
    private static ParsedItem Item(long priceMinor) => new("x", 1, priceMinor);

    [Fact]
    public void Reconciling_receipt_is_quiet()
    {
        var items = new[] { Item(650), Item(300) };
        TotalsValidator.Check(items, new Bill(0, 0, 0, 950)).Should().BeNull();
    }

    [Fact]
    public void Extras_count_toward_the_total()
    {
        var items = new[] { Item(1000) };
        // 1000 items + 80 tax + 150 tip + 120 service == 1350 printed total.
        TotalsValidator.Check(items, new Bill(80, 150, 120, 1350)).Should().BeNull();
    }

    [Fact]
    public void A_penny_of_rounding_slack_is_tolerated()
    {
        var items = new[] { Item(998) };
        TotalsValidator.Check(items, new Bill(0, 0, 0, 1000)).Should().BeNull();
    }

    [Fact]
    public void A_real_mismatch_warns()
    {
        // A dropped 5.00 discount: items exceed the total by more than the slack.
        var items = new[] { Item(1500) };
        TotalsValidator.Check(items, new Bill(0, 0, 0, 1000))
            .Should().Be("parsed items and extras do not reconcile with the printed total");
    }

    [Fact]
    public void No_printed_total_has_nothing_to_check()
    {
        var items = new[] { Item(1500) };
        TotalsValidator.Check(items, new Bill(0, 0, 0, 0)).Should().BeNull();
    }
}
