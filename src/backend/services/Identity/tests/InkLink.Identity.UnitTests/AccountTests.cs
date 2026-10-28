using InkLink.Identity.Application;
using InkLink.Identity.Domain;

namespace InkLink.Identity.UnitTests;

public sealed class AccountTests
{
    [Fact]
    public void Artist_activation_and_name_changes_preserve_the_account_identifier()
    {
        var profile = Profile.Create("Cliente");
        var id = profile.Id;
        Assert.False(profile.IsArtist);
        profile.ActivateArtist();
        profile.ActivateArtist();
        profile.Rename(" Artista ");
        Assert.True(profile.IsArtist);
        Assert.Equal(id, profile.Id);
        Assert.Equal("Artista", profile.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void External_identity_requires_issuer_and_subject(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => ExternalAccount.Link(value!, "subject", Guid.CreateVersion7()));
        Assert.ThrowsAny<ArgumentException>(() => ExternalAccount.Link("https://issuer.test", value!, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Updating_profile_does_not_access_persistence_without_identity()
    {
        var store = new AccountStore();
        var status = await new UpdateMyProfile(new User(null), store).ExecuteAsync("Name");
        Assert.Equal(UpdateProfileStatus.Unauthenticated, status);
        Assert.Null(store.RequestedId);
    }

    [Fact]
    public async Task Updating_profile_rejects_empty_names_without_accessing_persistence()
    {
        var store = new AccountStore();
        var status = await new UpdateMyProfile(new User(Guid.CreateVersion7()), store).ExecuteAsync(" ");
        Assert.Equal(UpdateProfileStatus.InvalidName, status);
        Assert.Null(store.RequestedId);
    }

    [Fact]
    public async Task Updating_profile_uses_only_the_current_user_and_distinguishes_missing_profile()
    {
        var id = Guid.CreateVersion7();
        var store = new AccountStore();
        Assert.Equal(UpdateProfileStatus.NotFound, await new UpdateMyProfile(new User(id), store).ExecuteAsync("Name"));
        Assert.Equal(id, store.RequestedId);
    }

    [Fact]
    public async Task Provisioning_creates_a_linked_client_with_a_backend_generated_id()
    {
        var store = new AccountStore();
        var result = await new ProvisionAccount(store).ExecuteAsync("https://issuer.test", "subject", " Name ");
        Assert.NotEqual(Guid.Empty, result.ProfileId);
        Assert.False(result.IsArtist);
        Assert.Equal(result.ProfileId, store.AddedProfile!.Id);
        Assert.Equal("Name", store.AddedProfile.DisplayName);
        Assert.Equal(result.ProfileId, store.AddedLink!.ProfileId);
        Assert.Equal("https://issuer.test", store.AddedLink.Issuer);
        Assert.Equal("subject", store.AddedLink.Subject);
    }

    [Fact]
    public async Task Reidentification_preserves_existing_profile_name_and_roles()
    {
        var profile = Profile.Create("Existing name");
        profile.ActivateArtist();
        var store = new AccountStore { Profile = profile };
        var result = await new ProvisionAccount(store).ExecuteAsync("https://issuer.test", "subject", "New claim value");
        Assert.Equal(new AccountIdentity(profile.Id, true), result);
        Assert.Equal("Existing name", profile.DisplayName);
        Assert.Null(store.AddedProfile);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Concurrent_provisioning_uses_the_persisted_winner_identity_and_roles()
    {
        var winner = Profile.Create("Winner");
        winner.ActivateArtist();
        var store = new AccountStore { AddSucceeds = false };
        store.ExternalReads.Enqueue(null);
        store.ExternalReads.Enqueue(winner);
        var result = await new ProvisionAccount(store).ExecuteAsync("https://issuer.test", "subject", "Contender");
        Assert.Equal(new AccountIdentity(winner.Id, true), result);
    }

    [Fact]
    public async Task Administrative_activation_does_not_create_a_missing_account()
    {
        var store = new AccountStore();
        Assert.False(await new ActivateArtist(store).ExecuteAsync("https://issuer.test", "missing"));
        Assert.Null(store.AddedProfile);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Administrative_activation_applies_the_domain_transition_and_persists_it()
    {
        var profile = Profile.Create("Client");
        var store = new AccountStore { Profile = profile };
        Assert.True(await new ActivateArtist(store).ExecuteAsync("https://issuer.test", "subject"));
        Assert.True(profile.IsArtist);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Updating_the_current_profile_preserves_its_id_and_artist_role()
    {
        var profile = Profile.Create("Original");
        profile.ActivateArtist();
        var store = new AccountStore { Profile = profile };
        var result = await new UpdateMyProfile(new User(profile.Id), store).ExecuteAsync(" Updated ");
        Assert.Equal(UpdateProfileStatus.Updated, result);
        Assert.Equal("Updated", profile.DisplayName);
        Assert.True(profile.IsArtist);
        Assert.Equal(profile.Id, store.RequestedId);
        Assert.Equal(1, store.Saves);
    }

    private sealed record User(Guid? UserId) : ICurrentUser;
    private sealed class AccountStore : IAccountStore
    {
        public Profile? Profile { get; init; }
        public Queue<Profile?> ExternalReads { get; } = new();
        public Guid? RequestedId { get; private set; }
        public Profile? AddedProfile { get; private set; }
        public ExternalAccount? AddedLink { get; private set; }
        public bool AddSucceeds { get; init; } = true;
        public int Saves { get; private set; }
        public Task<Profile?> FindByIdAsync(Guid profileId, CancellationToken cancellationToken)
        {
            RequestedId = profileId;
            return Task.FromResult(Profile?.Id == profileId ? Profile : null);
        }
        public Task<Profile?> FindByExternalIdentityAsync(string issuer, string subject, CancellationToken cancellationToken) =>
            Task.FromResult(ExternalReads.Count > 0 ? ExternalReads.Dequeue() : Profile);
        public Task<bool> TryAddAsync(Profile profile, ExternalAccount link, CancellationToken cancellationToken)
        {
            AddedProfile = profile;
            AddedLink = link;
            return Task.FromResult(AddSucceeds);
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken) { Saves++; return Task.CompletedTask; }
    }
}
