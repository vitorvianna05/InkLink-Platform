using InkLink.Catalog.Application;
using InkLink.Catalog.Application.Accounts;
using InkLink.Catalog.Application.Publishing;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;

namespace InkLink.Catalog.UnitTests;

public sealed class CatalogTests
{
    [Theory]
    [InlineData(" SÃO  Paulo ", "sao paulo")]
    [InlineData("BlackWork", "blackwork")]
    [InlineData("Estúdio-Á", "estudio a")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void Text_is_normalized(string? input, string expected) => Assert.Equal(expected, SearchText.Normalize(input));

    [Fact]
    public void Invalid_profile_does_not_partially_modify_the_domain()
    {
        var artist = Artist.Create(Guid.NewGuid(), "Artista", "Estúdio", "São Paulo", "Biografia", ["Blackwork"], 100);
        Assert.Throws<InvalidCatalogData>(() => artist.Update("Outro", "Estúdio", "Rio", "Biografia", ["Blackwork", "BLACKWORK"], 100));
        Assert.Equal("Artista", artist.Name);
        Assert.Equal("São Paulo", artist.City);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.001)]
    [InlineData(10000000)]
    public void Price_invariant_is_enforced(decimal price) => Assert.Throws<InvalidCatalogData>(() => Artist.Create(Guid.NewGuid(), "A", "B", "C", "D", ["E"], price));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Artwork_requires_an_uploaded_image(string key) => Assert.Throws<InvalidCatalogData>(() => Artwork.Create(Guid.NewGuid(), ArtworkKind.Flash, "Desenho", "Descrição", "Blackwork", 10, key));

    [Fact]
    public void Replacing_the_image_returns_the_previous_upload()
    {
        var artwork = Artwork.Create(Guid.NewGuid(), ArtworkKind.Tattoo, "Tattoo", "Descrição", "Blackwork", null, "artworks/a/1");
        Assert.Equal("artworks/a/1", artwork.ReplaceImage("artworks/a/2"));
        Assert.Equal("artworks/a/2", artwork.ImageKey);
        Assert.False(artwork.HasExternalImage);
    }

    [Fact]
    public void Artwork_kind_and_reference_price_remain_distinct()
    {
        Assert.Throws<InvalidCatalogData>(() => Artwork.Create(Guid.NewGuid(), ArtworkKind.Tattoo, "Tattoo", "Descrição", "Blackwork", 100, "artworks/a/1"));
        var flash = Artwork.Create(Guid.NewGuid(), ArtworkKind.Flash, "Flash", "Descrição", "Blackwork", 100, "artworks/a/1");
        Assert.Equal(ArtworkKind.Flash, flash.Kind);
        Assert.Equal(7, flash.Id.Version);
    }

    [Fact]
    public void Cursor_is_bound_to_resource_and_filters()
    {
        var position = new PagePosition(2, "artista", Guid.NewGuid());
        var cursor = CatalogCursor.Encode("artists", "criteria-a", position);
        Assert.Equal(position, CatalogCursor.Decode(cursor, "artists", "criteria-a"));
        Assert.Throws<CatalogFailure>(() => CatalogCursor.Decode(cursor, "artists", "criteria-b"));
        Assert.Throws<CatalogFailure>(() => CatalogCursor.Decode(cursor, "favorites", "criteria-a"));
        Assert.Throws<CatalogFailure>(() => CatalogCursor.Decode("malformed", "artists", "criteria-a"));
    }

    [Fact]
    public async Task Non_artist_is_rejected_before_loading_a_publication()
    {
        var store = new RecordingStore();
        var command = new PublishArtist(new Current(new CatalogAccount(Guid.NewGuid(), false)), store);
        var failure = await Assert.ThrowsAsync<CatalogFailure>(() => command.ExecuteAsync(new("A", "B", "C", "D", ["E"], 0), default));
        Assert.Equal(CatalogError.Forbidden, failure.Error);
        Assert.False(store.Read);
    }

    private sealed record Current(CatalogAccount? Account) : ICurrentCatalogAccount;
    private sealed class RecordingStore : IArtistStore
    {
        public bool Read { get; private set; }
        public Task<Artist?> FindByOwnerAsync(Guid id, CancellationToken ct) { Read = true; return Task.FromResult<Artist?>(null); }
        public Task SaveArtistAsync(Artist artist, bool created, CancellationToken ct) => Task.CompletedTask;
        public Task<Artwork?> FindArtworkAsync(Guid artistId, Guid artworkId, ArtworkKind kind, CancellationToken ct) => Task.FromResult<Artwork?>(null);
        public Task SaveArtworkAsync(Artwork artwork, bool created, ImageChange images, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteArtworkAsync(Artwork artwork, IReadOnlyList<string> obsoleteObjects, CancellationToken ct) => Task.CompletedTask;
    }
}
