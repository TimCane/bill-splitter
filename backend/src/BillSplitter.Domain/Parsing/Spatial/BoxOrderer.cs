using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Spatial;

/// <summary>Restores reading order to priced candidates the sidecar emitted
/// scrambled (docs/15-receipt-parsing.md#spatial-information): sort by <c>Box.Y</c>
/// then <c>Box.X</c>, top-to-bottom and left-to-right within a row. Gated - when the
/// candidates already run in that order (the common case, the whole current corpus)
/// the input list is returned untouched, so an ordered receipt is never perturbed.
/// This reorders the priced rows only; a name and its price split into separate
/// columns are not reunified here (that stays parked), so the pass cannot invent an
/// item, only reorder ones already read.</summary>
internal static class BoxOrderer
{
    public static IReadOnlyList<Candidate> Order(IReadOnlyList<Candidate> candidates)
    {
        if (IsOrdered(candidates))
        {
            return candidates;
        }

        // OrderBy is stable, so candidates on the same row keep their scan order
        // once X ties - no spurious reshuffle of an already-adjacent pair.
        return candidates.OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
    }

    private static bool IsOrdered(IReadOnlyList<Candidate> candidates)
    {
        for (var i = 1; i < candidates.Count; i++)
        {
            var previous = candidates[i - 1];
            var current = candidates[i];
            if (current.Y < previous.Y || (current.Y == previous.Y && current.X < previous.X))
            {
                return false;
            }
        }

        return true;
    }
}
