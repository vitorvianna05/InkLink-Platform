using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Catalog.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "catalog");

        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

        migrationBuilder.CreateTable(
            name: "artists",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Studio = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Biography = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Styles = table.Column<string[]>(type: "text[]", nullable: false),
                StartingPrice = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                NormalizedCity = table.Column<string>(type: "text", nullable: false),
                NormalizedName = table.Column<string>(type: "text", nullable: false, collation: "C"),
                NormalizedStyles = table.Column<string[]>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_artists", x => x.Id);
                table.CheckConstraint("ck_artist_price", "\"StartingPrice\" >= 0");
            });

        migrationBuilder.CreateTable(
            name: "artworks",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                AlternativeText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Style = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ReferencePrice = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_artworks", x => x.Id);
                table.CheckConstraint("ck_artwork_price", "\"ReferencePrice\" IS NULL OR \"ReferencePrice\" >= 0");
                table.ForeignKey(
                    name: "FK_artworks_artists_ArtistId",
                    column: x => x.ArtistId,
                    principalSchema: "catalog",
                    principalTable: "artists",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "favorites",
            schema: "catalog",
            columns: table => new
            {
                ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_favorites", x => new { x.ClientId, x.ArtistId });
                table.ForeignKey(
                    name: "FK_favorites_artists_ArtistId",
                    column: x => x.ArtistId,
                    principalSchema: "catalog",
                    principalTable: "artists",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "search_terms",
            schema: "catalog",
            columns: table => new
            {
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Normalized = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "C"),
                Value = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_search_terms", x => new { x.ArtistId, x.Category, x.Normalized });
                table.ForeignKey(
                    name: "FK_search_terms_artists_ArtistId",
                    column: x => x.ArtistId,
                    principalSchema: "catalog",
                    principalTable: "artists",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_artists_NormalizedName_Id",
            schema: "catalog",
            table: "artists",
            columns: new[] { "NormalizedName", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_artists_OwnerId",
            schema: "catalog",
            table: "artists",
            column: "OwnerId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_artists_StartingPrice",
            schema: "catalog",
            table: "artists",
            column: "StartingPrice");

        migrationBuilder.CreateIndex(
            name: "IX_artworks_ArtistId_Kind_Id",
            schema: "catalog",
            table: "artworks",
            columns: new[] { "ArtistId", "Kind", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_favorites_ArtistId",
            schema: "catalog",
            table: "favorites",
            column: "ArtistId");

        migrationBuilder.CreateIndex(
            name: "IX_search_terms_Normalized",
            schema: "catalog",
            table: "search_terms",
            column: "Normalized")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "artworks",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "favorites",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "search_terms",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "artists",
            schema: "catalog");
    }
}
