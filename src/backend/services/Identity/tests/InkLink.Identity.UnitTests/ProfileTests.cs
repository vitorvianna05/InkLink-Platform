using InkLink.Identity.Application;
using InkLink.Identity.Domain;

namespace InkLink.Identity.UnitTests;

public sealed class ProfileTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Rejects_missing_name(string? name) => Assert.ThrowsAny<ArgumentException>(() => Profile.Create(name!));

    [Fact]
    public void Creates_trimmed_profile_with_uuid_v7()
    {
        var profile = Profile.Create("  Júlia Vieira  ");
        Assert.Equal("Júlia Vieira", profile.DisplayName);
        Assert.Equal('7', profile.Id.ToString()[14]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Missing_identity_does_not_read_database(string? value)
    {
        var reader = new Reader();
        var handler = new GetMyProfile(new Current(value is null ? null : Guid.Parse(value)), reader);
        Assert.Equal(ProfileStatus.Unauthenticated, (await handler.ExecuteAsync()).Status);
        Assert.Null(reader.RequestedId);
    }

    [Fact]
    public async Task Uses_only_current_identity_and_preserves_cancellation()
    {
        var profile = Profile.Create("Júlia");
        var reader = new Reader { Profile = profile };
        using var cancellation = new CancellationTokenSource();
        var result = await new GetMyProfile(new Current(profile.Id), reader).ExecuteAsync(cancellation.Token);
        Assert.Equal(new ProfileResponse(profile.Id, "Júlia"), result.Profile);
        Assert.Equal(profile.Id, reader.RequestedId);
        Assert.Equal(cancellation.Token, reader.Token);
    }

    [Fact]
    public async Task Unknown_identity_has_no_profile()
    {
        var result = await new GetMyProfile(new Current(Guid.CreateVersion7()), new Reader()).ExecuteAsync();
        Assert.Equal(ProfileStatus.NotFound, result.Status);
        Assert.Null(result.Profile);
    }

    private sealed record Current(Guid? UserId) : ICurrentUser;
    private sealed class Reader : IProfileReader
    {
        public Profile? Profile { get; init; }
        public Guid? RequestedId { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<Profile?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            RequestedId = id;
            Token = cancellationToken;
            return Task.FromResult(Profile);
        }
    }
}
