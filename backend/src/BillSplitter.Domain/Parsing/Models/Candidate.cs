namespace BillSplitter.Domain.Parsing.Models;

/// <summary>A line carrying an end-anchored amount: its <c>Box.Y</c> and
/// <c>Box.X</c>, the amount in minor units, the name text left of the amount, the
/// raw line, the text of the line above and whether that line carried its own
/// amount (some labels print on the line above their value). <c>X</c> is the
/// same-row tiebreak when the box-orderer restores reading order
/// (docs/15-receipt-parsing.md#spatial-information).</summary>
internal sealed record Candidate(
    int Y, int X, long Amount, string Name, string Text, string PreviousText, bool PreviousHasAmount);
