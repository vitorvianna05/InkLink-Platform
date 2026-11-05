using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Catalog.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ArtistPhotoProjection : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "artist_photos",
            schema: "catalog",
            columns: table => new
            {
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                PhotoUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                ThumbnailUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                Revision = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_artist_photos", x => x.OwnerId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "artist_photos",
            schema: "catalog");
    }
}
