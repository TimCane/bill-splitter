using BillSplitter.Domain;

namespace BillSplitter.Tests.Domain;

/// <summary>Builds sessions in an arbitrary state for aggregate rule tests,
/// bypassing the lifecycle guards that the real flow would enforce.</summary>
internal static class SessionBuilder
{
    public static readonly DateTimeOffset Now = new(2026, 7, 4, 19, 0, 0, TimeSpan.Zero);

    public const string HostId = "host-participant-000000";

    public static Session InState(SessionState state, params LineItem[] items) =>
        new(
            id: "session-000000000000000",
            version: 3,
            state: state,
            currency: "GBP",
            shortCode: state is SessionState.Open or SessionState.Finalized ? "K7MPQ2" : null,
            createdAt: Now,
            finalizedAt: state == SessionState.Finalized ? Now : null,
            hostParticipantId: HostId,
            participants: [new Participant(HostId, "hosthash", "Host", Now)],
            items: [.. items],
            bill: new Bill(0, 0, 0, 0),
            ocr: new OcrInfo(OcrStatus.Done, null));

    public static LineItem Item(string id = "item-0000000000000000", long priceMinor = 1000) =>
        new(id, "Margherita", 1, priceMinor, null);
}
