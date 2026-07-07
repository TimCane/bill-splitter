using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Classification;

/// <summary>Maps a priced <see cref="Candidate"/> to its <see cref="LineType"/>
/// (docs/06-ocr-service.md#parsing, docs/15-receipt-parsing.md#classify-dont-itemise):
/// the keyword and positional decisions that say a line is a subtotal, tax,
/// tip, service, total, payment noise or a bare charge rather than an item.
/// Item-name shaping and unit-price rules are separate concerns, not here.</summary>
internal interface ILineClassifier
{
    LineType Classify(Candidate candidate);
}
