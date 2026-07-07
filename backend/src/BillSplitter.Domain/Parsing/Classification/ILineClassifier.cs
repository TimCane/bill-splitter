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

    /// <summary>Whether the line could be the receipt's grand total: a total-word
    /// row that is not a subtotal, item-count or VAT breakdown. Distinct from
    /// <see cref="Classify"/>, which ranks a tax/service word ahead of the total
    /// word ("Total Taxes" reads as tax) - grand-total detection must still see
    /// "Total incl. VAT" as a total.</summary>
    bool IsGrandTotalCandidate(Candidate candidate);
}
