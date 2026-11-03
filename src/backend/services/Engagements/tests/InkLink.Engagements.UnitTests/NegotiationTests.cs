using InkLink.Engagements.Domain;

namespace InkLink.Engagements.UnitTests;

public sealed class NegotiationTests
{
    private static Engagement New() => Engagement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Ideia", "Braço", 12, "{}", Guid.NewGuid(), "fingerprint", DateTimeOffset.UtcNow);
    private static Proposal Offer(Engagement e) => Proposal.Create(e.Id, Guid.NewGuid(), "hash", "Sessão", 300, DateTimeOffset.UtcNow);

    [Fact]
    public void New_request_can_negotiate_before_acceptance()
    {
        var engagement = New();
        Assert.Equal(EngagementState.Negotiating, engagement.State);
        Assert.Null(engagement.CurrentProposalId);
        Assert.Equal(1, engagement.Revision);
    }

    [Fact]
    public void Replacement_preserves_history_and_prevents_accepting_old_proposal()
    {
        var e = New(); var first = Offer(e); var second = Offer(e);
        e.Present(first, null); e.Present(second, first);
        Assert.Equal(ProposalState.Superseded, first.State);
        Assert.Equal(second.Id, e.CurrentProposalId);
        Assert.Throws<InvalidOperationException>(() => e.Accept(first, DateTimeOffset.UtcNow));
        e.Accept(second, DateTimeOffset.UtcNow);
        Assert.Equal(ProposalState.Accepted, second.State);
        Assert.Equal(EngagementState.Agreed, e.State);
    }

    [Fact]
    public void Acceptance_is_idempotent_and_freezes_negotiation()
    {
        var e = New(); var p = Offer(e); e.Present(p, null);
        var now = EngagementRules.Instant(DateTimeOffset.UtcNow); e.Accept(p, now); var revision = e.Revision;
        e.Accept(p, now.AddDays(1));
        Assert.Equal(now, p.AcceptedAt); Assert.Equal(revision, e.Revision);
        Assert.Throws<InvalidOperationException>(() => e.Present(Offer(e), p));
    }

    [Fact]
    public void Accepting_other_engagement_does_not_modify_current_one()
    {
        var e = New(); var other = New(); var p = Offer(other); other.Present(p, null);
        Assert.Throws<InvalidOperationException>(() => e.Accept(p, DateTimeOffset.UtcNow));
        Assert.Equal(EngagementState.Negotiating, e.State);
        Assert.Equal(ProposalState.Pending, p.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(300.01)]
    [InlineData(12.123)]
    public void Invalid_size_is_rejected(decimal size) => Assert.Throws<InvalidEngagementData>(() =>
        Engagement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Ideia", "Braço", size, "{}", Guid.NewGuid(), "hash", DateTimeOffset.UtcNow));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10000000)]
    [InlineData(12.123)]
    public void Invalid_price_is_rejected(decimal amount) => Assert.Throws<InvalidEngagementData>(() =>
        Proposal.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "Descrição", amount, DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_text_is_rejected(string text) => Assert.Throws<InvalidEngagementData>(() => EngagementRules.Text(text, "idea", 4000));

    [Fact]
    public void Presenting_foreign_previous_proposal_cannot_mutate_another_engagement()
    {
        var first = New(); var foreign = Offer(first); first.Present(foreign, null);
        var second = New(); var revision = second.Revision;
        Assert.Throws<InvalidOperationException>(() => second.Present(Offer(second), foreign));
        Assert.Equal(ProposalState.Pending, foreign.State);
        Assert.Null(second.CurrentProposalId);
        Assert.Equal(revision, second.Revision);
    }

    [Fact]
    public void Re_presenting_current_or_superseded_proposal_is_rejected_without_mutation()
    {
        var e = New(); var first = Offer(e); e.Present(first, null); var revision = e.Revision;
        Assert.Throws<InvalidOperationException>(() => e.Present(first, first));
        Assert.Equal(ProposalState.Pending, first.State); Assert.Equal(revision, e.Revision);
        var second = Offer(e); e.Present(second, first); revision = e.Revision;
        Assert.Throws<InvalidOperationException>(() => e.Present(first, second));
        Assert.Equal(second.Id, e.CurrentProposalId); Assert.Equal(ProposalState.Pending, second.State); Assert.Equal(revision, e.Revision);
    }

    [Fact]
    public void Input_length_is_checked_before_trimming_to_match_the_schema()
    {
        Assert.Throws<InvalidEngagementData>(() => EngagementRules.Text(new string(' ', 4000) + "x", "idea", 4000));
        Assert.Equal("x", EngagementRules.Text(" x ", "idea", 4000));
    }

    [Fact]
    public void Self_service_is_rejected()
    {
        var account = Guid.NewGuid();
        Assert.Throws<InvalidEngagementData>(() => Engagement.Create(account, account, Guid.NewGuid(), null, "Ideia", "Braço", 12, "{}", Guid.NewGuid(), "hash", DateTimeOffset.UtcNow));
    }
}
