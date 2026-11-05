using InkLink.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Artwork> Artworks => Set<Artwork>();
    public DbSet<ObjectCleanup> ObjectCleanups => Set<ObjectCleanup>();
    public DbSet<Media.ArtistPhoto> ArtistPhotos => Set<Media.ArtistPhoto>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    internal DbSet<SearchTerm> SearchTerms => Set<SearchTerm>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("catalog");
        model.HasPostgresExtension("pg_trgm");
        model.Entity<Artist>(entity =>
        {
            entity.ToTable("artists", table => table.HasCheckConstraint("ck_artist_price", "\"StartingPrice\" >= 0"));
            entity.HasKey(artist => artist.Id);
            entity.Property(artist => artist.Id).ValueGeneratedNever();
            entity.HasIndex(artist => artist.OwnerId).IsUnique();
            entity.Property(artist => artist.Name).HasMaxLength(120).IsRequired();
            entity.Property(artist => artist.Studio).HasMaxLength(160).IsRequired();
            entity.Property(artist => artist.City).HasMaxLength(100).IsRequired();
            entity.Property(artist => artist.Biography).HasMaxLength(4000).IsRequired();
            entity.Property(artist => artist.Styles).IsRequired();
            entity.Property(artist => artist.StartingPrice).HasPrecision(9, 2);
            entity.Property<string>("NormalizedName").UseCollation("C").IsRequired();
            entity.Property<string>("NormalizedCity").IsRequired();
            entity.Property<string[]>("NormalizedStyles").IsRequired();
            entity.HasIndex("NormalizedName", nameof(Artist.Id));
            entity.HasIndex(artist => artist.StartingPrice);
        });
        model.Entity<Artwork>(entity =>
        {
            entity.ToTable("artworks", table =>
            {
                table.HasCheckConstraint("ck_artwork_price", "\"ReferencePrice\" IS NULL OR \"ReferencePrice\" >= 0");
                // Imagem enviada (ImageKey) ou URL externa legada, nunca as duas nem nenhuma (decisão 0018).
                table.HasCheckConstraint("ck_artwork_image", "(\"ImageKey\" IS NULL) <> (\"ImageUrl\" IS NULL)");
            });
            entity.HasKey(artwork => artwork.Id);
            entity.Property(artwork => artwork.Id).ValueGeneratedNever();
            entity.HasOne<Artist>().WithMany().HasForeignKey(artwork => artwork.ArtistId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(artwork => new { artwork.ArtistId, artwork.Kind, artwork.Id });
            entity.Property(artwork => artwork.Title).HasMaxLength(160).IsRequired();
            entity.Property(artwork => artwork.ImageUrl).HasMaxLength(2048);
            entity.Property(artwork => artwork.ImageKey).HasMaxLength(200);
            entity.Ignore(artwork => artwork.HasExternalImage);
            entity.Property(artwork => artwork.AlternativeText).HasMaxLength(300).IsRequired();
            entity.Property(artwork => artwork.Style).HasMaxLength(80).IsRequired();
            entity.Property(artwork => artwork.ReferencePrice).HasPrecision(9, 2);
        });
        model.Entity<Media.ArtistPhoto>(entity =>
        {
            entity.ToTable("artist_photos");
            entity.HasKey(photo => photo.OwnerId);
            entity.Property(photo => photo.OwnerId).ValueGeneratedNever();
            entity.Property(photo => photo.PhotoUrl).HasMaxLength(2048);
            entity.Property(photo => photo.ThumbnailUrl).HasMaxLength(2048);
        });
        model.Entity<ObjectCleanup>(entity =>
        {
            entity.ToTable("object_cleanups");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.Property(item => item.Key).HasMaxLength(512).IsRequired();
            entity.HasIndex(item => item.Key).IsUnique();
            entity.HasIndex(item => item.DueAt);
        });
        model.Entity<Favorite>(entity =>
        {
            entity.ToTable("favorites");
            entity.HasKey(favorite => new { favorite.ClientId, favorite.ArtistId });
            entity.HasOne<Artist>().WithMany().HasForeignKey(favorite => favorite.ArtistId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SearchTerm>(entity =>
        {
            entity.ToTable("search_terms");
            entity.HasKey(term => new { term.ArtistId, term.Category, term.Normalized });
            entity.HasOne<Artist>().WithMany().HasForeignKey(term => term.ArtistId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(term => term.Category).HasMaxLength(20);
            entity.Property(term => term.Value).HasMaxLength(160);
            entity.Property(term => term.Normalized).HasMaxLength(200).UseCollation("C");
            entity.HasIndex(term => term.Normalized).HasMethod("gin").HasOperators("gin_trgm_ops");
        });
    }
}

internal sealed class SearchTerm
{
    public Guid ArtistId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Normalized { get; set; } = string.Empty;
}
