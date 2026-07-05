namespace BillSplitter.Domain;

/// <summary>
/// Pure, stateless split math: largest-remainder distribution plus per-participant
/// totals (docs/02-domain-model.md#split-math-the-core-algorithm). The server owns
/// all money math; the frontend only formats.
/// </summary>
public static class SplitCalculator
{
    /// <summary>
    /// Largest-remainder distribution: split <paramref name="amount"/> across
    /// <paramref name="weights"/> so the integer allocations sum exactly to it.
    /// Zero total weight falls back to equal weights; an empty weight list returns
    /// an empty array. <paramref name="amount"/> must be non-negative.
    /// </summary>
    public static long[] Distribute(long amount, IReadOnlyList<long> weights)
    {
        var n = weights.Count;
        if (n == 0)
        {
            return [];
        }

        long totalWeight = 0;
        for (var i = 0; i < n; i++)
        {
            totalWeight += weights[i];
        }

        // All-zero weights (e.g. only zero-priced items claimed): split equally.
        var equalFallback = totalWeight == 0;
        if (equalFallback)
        {
            totalWeight = n;
        }

        var result = new long[n];
        var fractions = new (long Remainder, int Index)[n];
        long allocated = 0;
        for (var i = 0; i < n; i++)
        {
            var weight = equalFallback ? 1L : weights[i];
            var numerator = amount * weight;
            var share = numerator / totalWeight;
            result[i] = share;
            allocated += share;
            fractions[i] = (numerator % totalWeight, i);
        }

        // Hand the leftover minor units to the largest fractional parts, breaking
        // ties by lowest index (stable order).
        var leftover = amount - allocated;
        Array.Sort(fractions, static (a, b) =>
        {
            var byRemainder = b.Remainder.CompareTo(a.Remainder);
            return byRemainder != 0 ? byRemainder : a.Index.CompareTo(b.Index);
        });
        for (var k = 0; k < leftover; k++)
        {
            result[fractions[k].Index]++;
        }

        return result;
    }

    /// <summary>Compute per-participant totals and per-claim allocations for the
    /// current session state. Open suppresses extras/unclaimed display until there
    /// is something to split; Finalized distributes the whole bill.</summary>
    public static SplitResult Compute(Session session)
    {
        var finalized = session.State == SessionState.Finalized;
        var participants = session.Participants
            .OrderBy(p => p.JoinedAt)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        var items = new Dictionary<string, long>(participants.Count);
        foreach (var p in participants)
        {
            items[p.Id] = 0;
        }

        var allocations = new Dictionary<(string, string), long>();
        long unclaimedTotal = 0;

        foreach (var item in session.Items)
        {
            if (item.Claims.Count == 0)
            {
                unclaimedTotal += item.PriceMinor;
                continue;
            }

            var claimants = item.Claims
                .OrderBy(c => ParticipantOrder(participants, c.ParticipantId))
                .ToList();
            var weights = claimants.Select(c => (long)c.Shares).ToList();
            var shares = Distribute(item.PriceMinor, weights);
            for (var i = 0; i < claimants.Count; i++)
            {
                var pid = claimants[i].ParticipantId;
                items[pid] += shares[i];
                allocations[(item.Id, pid)] = shares[i];
            }
        }

        var claimedSubtotal = items.Values.Sum();
        var itemWeights = participants.Select(p => items[p.Id]).ToList();

        var tax = ExtrasFor(session.Bill.TaxMinor);
        var tip = ExtrasFor(session.Bill.TipMinor);
        var service = ExtrasFor(session.Bill.ServiceMinor);

        var unclaimed = finalized
            ? Distribute(unclaimedTotal, participants.Select(_ => 1L).ToList())
            : new long[participants.Count];

        var totals = new Dictionary<string, ParticipantTotals>(participants.Count);
        for (var i = 0; i < participants.Count; i++)
        {
            var pid = participants[i].Id;
            var total = items[pid] + tax[i] + tip[i] + service[i] + unclaimed[i];
            totals[pid] = new ParticipantTotals(items[pid], tax[i], tip[i], service[i], unclaimed[i], total);
        }

        return new SplitResult(totals, allocations, unclaimedTotal);

        long[] ExtrasFor(long amount)
        {
            // While Open with nothing claimed, extras stay unallocated for display;
            // the equal-split fallback only fires at finalize.
            if (!finalized && claimedSubtotal == 0)
            {
                return new long[participants.Count];
            }

            return Distribute(amount, itemWeights);
        }
    }

    private static int ParticipantOrder(List<Participant> ordered, string participantId)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Id == participantId)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
