namespace BillSplitter.Domain.Parsing.Models;

/// <summary>A line carrying an end-anchored amount: its <c>Box.Y</c>, the amount
/// in minor units, the name text left of the amount, the raw line, the text of
/// the line above and whether that line carried its own amount (some labels print
/// on the line above their value).</summary>
internal sealed record Candidate(
    int Y, long Amount, string Name, string Text, string PreviousText, bool PreviousHasAmount);
