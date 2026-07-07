namespace BillSplitter.Domain.Parsing.Models;

/// <summary>A line carrying an end-anchored amount: its <c>Box.Y</c>, the amount
/// in minor units, the name text left of the amount, the raw line, and the text
/// of the line above (some labels print above their amount).</summary>
internal sealed record Candidate(int Y, long Amount, string Name, string Text, string PreviousText);
