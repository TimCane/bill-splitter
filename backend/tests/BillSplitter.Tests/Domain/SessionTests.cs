using System.Text.Json;
using BillSplitter.Domain;
using FluentAssertions;

namespace BillSplitter.Tests.Domain;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Now = SessionBuilder.Now;

    [Fact]
    public void Create_starts_in_processing_with_pending_host()
    {
        var session = Session.Create("s1", "p1", "hash", Now);

        session.State.Should().Be(SessionState.Processing);
        session.Currency.Should().Be("GBP");
        session.Version.Should().Be(0);
        session.HostParticipantId.Should().Be("p1");
        session.Ocr.Status.Should().Be(OcrStatus.Pending);
        session.Participants.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Id = "p1", DisplayName = "Host" });
    }

    [Fact]
    public void CompleteOcr_advances_processing_to_review()
    {
        var session = Session.Create("s1", "p1", "hash", Now);

        session.CompleteOcr([], new Bill(0, 0, 0, 0), "GBP");

        session.State.Should().Be(SessionState.Review);
        session.Ocr.Status.Should().Be(OcrStatus.Done);
    }

    [Fact]
    public void CompleteOcr_is_a_noop_once_past_processing()
    {
        var session = SessionBuilder.InState(SessionState.Open);

        session.CompleteOcr([SessionBuilder.Item()], new Bill(1, 1, 1, 1), "USD");

        session.State.Should().Be(SessionState.Open);
        session.Items.Should().BeEmpty();
    }

    // --- Join --------------------------------------------------------------

    [Theory]
    [InlineData(SessionState.Processing)]
    [InlineData(SessionState.Review)]
    [InlineData(SessionState.Finalized)]
    public void Join_outside_open_is_wrong_state(SessionState state)
    {
        var session = SessionBuilder.InState(state);

        var act = () => session.Join("p2", "h2", "Sam", Now, maxParticipants: 20);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.WrongState);
    }

    [Fact]
    public void Join_in_open_adds_participant()
    {
        var session = SessionBuilder.InState(SessionState.Open);

        session.Join("p2", "h2", "  Sam  ", Now, maxParticipants: 20);

        session.Participants.Should().HaveCount(2);
        session.Participants[1].DisplayName.Should().Be("Sam");
    }

    [Fact]
    public void Join_past_cap_is_session_full()
    {
        var session = SessionBuilder.InState(SessionState.Open);

        var act = () => session.Join("p2", "h2", "Sam", Now, maxParticipants: 1);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.SessionFull);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Join_with_blank_name_is_validation(string name)
    {
        var session = SessionBuilder.InState(SessionState.Open);

        var act = () => session.Join("p2", "h2", name, Now, maxParticipants: 20);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.Validation);
    }

    // --- Rename ------------------------------------------------------------

    [Theory]
    [InlineData(SessionState.Review)]
    [InlineData(SessionState.Open)]
    public void Rename_allowed_in_review_and_open(SessionState state)
    {
        var session = SessionBuilder.InState(state);

        session.RenameParticipant(SessionBuilder.HostId, "Tim");

        session.Participants[0].DisplayName.Should().Be("Tim");
    }

    [Theory]
    [InlineData(SessionState.Processing)]
    [InlineData(SessionState.Finalized)]
    public void Rename_outside_review_open_is_wrong_state(SessionState state)
    {
        var session = SessionBuilder.InState(state);

        var act = () => session.RenameParticipant(SessionBuilder.HostId, "Tim");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.WrongState);
    }

    [Fact]
    public void Rename_unknown_participant_throws()
    {
        var session = SessionBuilder.InState(SessionState.Open);

        var act = () => session.RenameParticipant("ghost", "Tim");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.UnknownParticipant);
    }

    // --- Item CRUD ---------------------------------------------------------

    [Theory]
    [InlineData(SessionState.Processing)]
    [InlineData(SessionState.Open)]
    [InlineData(SessionState.Finalized)]
    public void AddItem_outside_review_is_wrong_state(SessionState state)
    {
        var session = SessionBuilder.InState(state);

        var act = () => session.AddItem("i1", "Beer", 1, 500, maxItems: 100);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.WrongState);
    }

    [Fact]
    public void AddItem_past_cap_is_validation()
    {
        var session = SessionBuilder.InState(SessionState.Review, SessionBuilder.Item());

        var act = () => session.AddItem("i2", "Beer", 1, 500, maxItems: 1);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.Validation);
    }

    [Theory]
    [InlineData("", 1, 0)]
    [InlineData("Beer", 0, 100)]
    [InlineData("Beer", 1, -1)]
    [InlineData("Beer", 1, Session.MaxAmountMinor + 1)]
    public void AddItem_bounds_are_validation(string name, int quantity, long price)
    {
        var session = SessionBuilder.InState(SessionState.Review);

        var act = () => session.AddItem("i1", name, quantity, price, maxItems: 100);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public void RemoveItem_missing_is_item_not_found()
    {
        var session = SessionBuilder.InState(SessionState.Review);

        var act = () => session.RemoveItem("nope");

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ItemNotFound);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    public void SetBill_accepts_a_known_iso_currency(string currency)
    {
        var session = SessionBuilder.InState(SessionState.Review);

        session.SetBill(0, 500, 0, 5450, currency);

        session.Currency.Should().Be(currency);
    }

    [Theory]
    [InlineData("XYZ")]  // well-formed but not an assigned code
    [InlineData("gbp")]  // wrong case
    [InlineData("GB")]   // too short
    public void SetBill_rejects_a_non_iso_currency(string currency)
    {
        var session = SessionBuilder.InState(SessionState.Review);

        var act = () => session.SetBill(0, 0, 0, 0, currency);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.Validation);
    }

    // --- Claims ------------------------------------------------------------

    [Fact]
    public void SetShares_in_open_upserts_claim()
    {
        var item = SessionBuilder.Item();
        var session = SessionBuilder.InState(SessionState.Open, item);

        session.SetShares(item.Id, SessionBuilder.HostId, 3);
        session.SetShares(item.Id, SessionBuilder.HostId, 5);

        item.Claims.Should().ContainSingle().Which.Shares.Should().Be(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void SetShares_out_of_range_is_validation(int shares)
    {
        var item = SessionBuilder.Item();
        var session = SessionBuilder.InState(SessionState.Open, item);

        var act = () => session.SetShares(item.Id, SessionBuilder.HostId, shares);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public void SetShares_outside_open_is_wrong_state()
    {
        var item = SessionBuilder.Item();
        var session = SessionBuilder.InState(SessionState.Review, item);

        var act = () => session.SetShares(item.Id, SessionBuilder.HostId, 1);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.WrongState);
    }

    [Fact]
    public void Unclaim_is_noop_when_not_claimed()
    {
        var item = SessionBuilder.Item();
        var session = SessionBuilder.InState(SessionState.Open, item);

        session.UnclaimItem(item.Id, SessionBuilder.HostId);

        item.Claims.Should().BeEmpty();
    }

    // --- Open / finalize host + state --------------------------------------

    [Fact]
    public void Open_requires_review_and_host()
    {
        var review = SessionBuilder.InState(SessionState.Review);
        review.Open(SessionBuilder.HostId, "K7MPQ2");
        review.State.Should().Be(SessionState.Open);
        review.ShortCode.Should().Be("K7MPQ2");

        var nonHost = SessionBuilder.InState(SessionState.Review);
        var act = () => nonHost.Open("someone-else", "K7MPQ2");
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.NotHost);
    }

    [Fact]
    public void Finalize_requires_open_and_host()
    {
        var open = SessionBuilder.InState(SessionState.Open);
        open.Finalize(SessionBuilder.HostId, Now);
        open.State.Should().Be(SessionState.Finalized);

        var wrongState = SessionBuilder.InState(SessionState.Review);
        var act = () => wrongState.Finalize(SessionBuilder.HostId, Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.WrongState);
    }

    // --- Redis JSON round-trip --------------------------------------------

    [Fact]
    public void Round_trips_through_camelcase_json()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };
        var item = SessionBuilder.Item();
        var original = SessionBuilder.InState(SessionState.Open, item);
        original.SetShares(item.Id, SessionBuilder.HostId, 2);

        var json = JsonSerializer.Serialize(original, options);
        var restored = JsonSerializer.Deserialize<Session>(json, options)!;

        json.Should().Contain("\"version\":3").And.Contain("\"state\":\"Open\"");
        restored.Should().BeEquivalentTo(original, o => o.RespectingRuntimeTypes());
    }
}
