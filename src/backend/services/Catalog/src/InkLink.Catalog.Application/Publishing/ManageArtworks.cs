using InkLink.Catalog.Application.Accounts;
using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;

namespace InkLink.Catalog.Application.Publishing;

/// <summary>
/// Imagens de obras (decisão 0018). A implementação normaliza o arquivo, reserva a limpeza dos objetos
/// antes de enviá-los e devolve o prefixo; a gravação da obra confirma a reserva na mesma transação.
/// </summary>
public interface IArtworkImages
{
    Task<string> StoreAsync(Guid artistId, byte[] content, CancellationToken cancellationToken);
    IReadOnlyList<string> Objects(string imageKey);
}

public sealed class ManageArtworks(ICurrentCatalogAccount current, IArtistStore store, IArtworkImages images, ICatalogQueries queries)
{
    public async Task<PublishedArtwork> CreateAsync(ArtworkKind kind, ArtworkInput input, byte[]? image, CancellationToken cancellationToken)
    {
        var artist = await OwnArtistAsync(cancellationToken);
        Artwork.ValidateDetails(kind, input.Title, input.AlternativeText, input.Style, input.ReferencePrice);
        if (image is null) throw new InvalidCatalogData("image", "The image file is required.");
        var key = await images.StoreAsync(artist.Id, image, cancellationToken);
        var artwork = Artwork.Create(artist.Id, kind, input.Title, input.AlternativeText, input.Style, input.ReferencePrice, key);
        await store.SaveArtworkAsync(artwork, true, new ImageChange(images.Objects(key), []), cancellationToken);
        return new(artist.Id, await ViewAsync(artist.Id, artwork, cancellationToken));
    }

    /// <summary>Atualiza os dados; a imagem é trocada somente quando um arquivo novo é enviado.</summary>
    public async Task UpdateAsync(Guid artworkId, ArtworkKind kind, ArtworkInput input, byte[]? image, CancellationToken cancellationToken)
    {
        var artist = await OwnArtistAsync(cancellationToken);
        var artwork = await store.FindArtworkAsync(artist.Id, artworkId, kind, cancellationToken)
            ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artwork_not_found");
        Artwork.ValidateDetails(kind, input.Title, input.AlternativeText, input.Style, input.ReferencePrice);
        var change = ImageChange.None;
        if (image is not null)
        {
            var key = await images.StoreAsync(artist.Id, image, cancellationToken);
            var previous = artwork.ReplaceImage(key);
            change = new(images.Objects(key), previous is null ? [] : images.Objects(previous));
        }
        artwork.UpdateDetails(input.Title, input.AlternativeText, input.Style, input.ReferencePrice);
        await store.SaveArtworkAsync(artwork, false, change, cancellationToken);
    }

    public async Task DeleteAsync(Guid artworkId, ArtworkKind kind, CancellationToken cancellationToken)
    {
        var artist = await OwnArtistAsync(cancellationToken);
        var artwork = await store.FindArtworkAsync(artist.Id, artworkId, kind, cancellationToken)
            ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artwork_not_found");
        await store.DeleteArtworkAsync(artwork, artwork.ImageKey is null ? [] : images.Objects(artwork.ImageKey), cancellationToken);
    }

    private async Task<Artist> OwnArtistAsync(CancellationToken cancellationToken)
    {
        var account = AccountAccess.Require(current, artist: true);
        return await store.FindByOwnerAsync(account.Id, cancellationToken)
            ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artist_not_found");
    }

    private async Task<ArtworkView> ViewAsync(Guid artistId, Artwork artwork, CancellationToken cancellationToken) =>
        await queries.FindArtworkAsync(artistId, artwork.Id, artwork.Kind == ArtworkKind.Flash, cancellationToken)
            ?? throw new CatalogFailure(CatalogError.NotFound, "catalog.artwork_not_found");
}

public sealed record PublishedArtwork(Guid ArtistId, ArtworkView Artwork);
