using BillSplitter.Domain;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Random = System.Random;

namespace BillSplitter.Tests.Domain;

public sealed class SplitCalculatorTests
{
    private static readonly DateTimeOffset Base = new(2026, 7, 4, 19, 0, 0, TimeSpan.Zero);

    // --- Distribute: worked cases -----------------------------------------

    [Fact]
    public void Distribute_exact_division()
    {
        SplitCalculator.Distribute(100, [1, 1, 1, 1]).Should().Equal(25, 25, 25, 25);
    }

    [Fact]
    public void Distribute_remainder_breaks_ties_by_lowest_index()
    {
        // 10 / 3 = 3 each, remainder 1 to the lowest index (equal fractions).
        SplitCalculator.Distribute(10, [1, 1, 1]).Should().Equal(4, 3, 3);
    }

    [Fact]
    public void Distribute_weights_the_largest_fractional_part()
    {
        // 2:1 of 100 -> 66.67 / 33.33 -> largest remainder is index 0.
        SplitCalculator.Distribute(100, [2, 1]).Should().Equal(67, 33);
    }

    [Fact]
    public void Distribute_single_weight_takes_all()
    {
        SplitCalculator.Distribute(1250, [1]).Should().Equal(1250);
    }

    [Fact]
    public void Distribute_all_zero_weights_falls_back_to_equal()
    {
        SplitCalculator.Distribute(10, [0, 0, 0]).Should().Equal(4, 3, 3);
    }

    [Fact]
    public void Distribute_empty_weights_is_empty()
    {
        SplitCalculator.Distribute(100, []).Should().BeEmpty();
    }

    [Fact]
    public void Distribute_one_penny_among_three()
    {
        SplitCalculator.Distribute(1, [1, 1, 1]).Should().Equal(1, 0, 0);
    }

    // --- Distribute: properties -------------------------------------------

    [Property]
    public Property Distribute_sums_exactly_and_is_non_negative()
    {
        var gen =
            from amount in Gen.Choose(0, 1_000_000)
            from count in Gen.Choose(1, 20)
            from weights in Gen.ArrayOf(count, Gen.Choose(0, 99))
            select (amount: (long)amount, weights: weights.Select(w => (long)w).ToArray());

        return Prop.ForAll(Arb.From(gen), t =>
        {
            var result = SplitCalculator.Distribute(t.amount, t.weights);
            var sumsExactly = result.Sum() == t.amount;
            var allNonNegative = result.All(x => x >= 0);
            var deterministic = SplitCalculator.Distribute(t.amount, t.weights).SequenceEqual(result);
            return sumsExactly && allNonNegative && deterministic;
        });
    }

    // --- Compute: named regression cases ----------------------------------

    [Fact]
    public void Open_with_nothing_claimed_leaves_extras_unallocated()
    {
        var ps = Participants(2);
        var item = new LineItem("i1", "Beer", 1, 500, null); // unclaimed
        var session = Build(SessionState.Open, ps, [item], new Bill(0, 100, 0, 600));

        var result = SplitCalculator.Compute(session);

        result.UnclaimedTotalMinor.Should().Be(500);
        result.Totals.Values.Should().OnlyContain(t => t.TotalMinor == 0);
    }

    [Fact]
    public void Finalized_with_nobody_claiming_splits_extras_equally()
    {
        var ps = Participants(3);
        var session = Build(SessionState.Finalized, ps, [], new Bill(0, 100, 0, 100));

        var result = SplitCalculator.Compute(session);

        result.Totals.Values.Select(t => t.TipMinor).Order().Should().Equal(33, 33, 34);
        result.Totals.Values.Sum(t => t.TotalMinor).Should().Be(100);
    }

    [Fact]
    public void Shared_item_uses_largest_remainder_across_claimants()
    {
        var ps = Participants(3);
        var item = new LineItem("i1", "Platter", 1, 1000, [
            new Claim(ps[0].Id, 2),
            new Claim(ps[1].Id, 1),
            new Claim(ps[2].Id, 1),
        ]);
        var session = Build(SessionState.Open, ps, [item], new Bill(0, 0, 0, 1000));

        var result = SplitCalculator.Compute(session);

        result.Allocations[("i1", ps[0].Id)].Should().Be(500);
        result.Allocations[("i1", ps[1].Id)].Should().Be(250);
        result.Allocations[("i1", ps[2].Id)].Should().Be(250);
    }

    // --- Compute: properties over random sessions -------------------------

    [Property]
    public bool Items_allocations_sum_to_claimed_and_are_deterministic(int seed)
    {
        var session = RandomSession(new Random(seed), finalized: false);
        var result = SplitCalculator.Compute(session);

        var claimedPrice = session.Items.Where(i => i.Claims.Count > 0).Sum(i => i.PriceMinor);
        var summedItems = result.Totals.Values.Sum(t => t.ItemsMinor);
        var nonNegative = result.Totals.Values.All(t =>
            t.ItemsMinor >= 0 && t.TaxMinor >= 0 && t.TipMinor >= 0 &&
            t.ServiceMinor >= 0 && t.UnclaimedMinor >= 0 && t.TotalMinor >= 0);
        var again = SplitCalculator.Compute(session);
        var deterministic = again.Totals.Values.Sum(t => t.TotalMinor) == result.Totals.Values.Sum(t => t.TotalMinor);

        return summedItems == claimedPrice && nonNegative && deterministic;
    }

    [Property]
    public bool Finalized_totals_cover_the_whole_bill(int seed)
    {
        var session = RandomSession(new Random(seed), finalized: true);
        var result = SplitCalculator.Compute(session);

        var subtotal = session.Items.Sum(i => i.PriceMinor);
        var whole = subtotal + session.Bill.TaxMinor + session.Bill.TipMinor + session.Bill.ServiceMinor;
        return result.Totals.Values.Sum(t => t.TotalMinor) == whole;
    }

    // --- Helpers -----------------------------------------------------------

    private static List<Participant> Participants(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new Participant($"p{i:D2}00000000000000000", $"h{i}", $"P{i}", Base.AddSeconds(i)))
            .ToList();

    private static Session Build(SessionState state, List<Participant> ps, List<LineItem> items, Bill bill) =>
        new(
            id: "session-000000000000000",
            version: 1,
            state: state,
            currency: "GBP",
            shortCode: null,
            createdAt: Base,
            finalizedAt: state == SessionState.Finalized ? Base : null,
            hostParticipantId: ps[0].Id,
            participants: ps,
            items: items,
            bill: bill,
            ocr: new OcrInfo(OcrStatus.Done, null));

    private static Session RandomSession(Random rng, bool finalized)
    {
        var ps = Participants(rng.Next(1, 7));
        var itemCount = rng.Next(0, 9);
        var items = new List<LineItem>(itemCount);
        for (var i = 0; i < itemCount; i++)
        {
            var claims = new List<Claim>();
            foreach (var p in ps)
            {
                if (rng.Next(2) == 0)
                {
                    claims.Add(new Claim(p.Id, rng.Next(1, 6)));
                }
            }

            items.Add(new LineItem($"i{i:D2}0000000000000000", $"Item{i}", 1, rng.Next(0, 5001), claims));
        }

        var bill = new Bill(rng.Next(0, 1001), rng.Next(0, 1001), rng.Next(0, 1001), rng.Next(0, 20001));
        return Build(finalized ? SessionState.Finalized : SessionState.Open, ps, items, bill);
    }
}
