using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Catalog.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ArtworkImageUploads : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "ImageUrl",
            schema: "catalog",
            table: "artworks",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(2048)",
            oldMaxLength: 2048);

        migrationBuilder.AddColumn<string>(
            name: "ImageKey",
            schema: "catalog",
            table: "artworks",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "object_cleanups",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_object_cleanups", x => x.Id);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_artwork_image",
            schema: "catalog",
            table: "artworks",
            sql: "(\"ImageKey\" IS NULL) <> (\"ImageUrl\" IS NULL)");

        migrationBuilder.CreateIndex(
            name: "IX_object_cleanups_DueAt",
            schema: "catalog",
            table: "object_cleanups",
            column: "DueAt");

        migrationBuilder.CreateIndex(
            name: "IX_object_cleanups_Key",
            schema: "catalog",
            table: "object_cleanups",
            column: "Key",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "object_cleanups",
            schema: "catalog");

        migrationBuilder.DropCheckConstraint(
            name: "ck_artwork_image",
            schema: "catalog",
            table: "artworks");

        migrationBuilder.DropColumn(
            name: "ImageKey",
            schema: "catalog",
            table: "artworks");

        migrationBuilder.AlterColumn<string>(
            name: "ImageUrl",
            schema: "catalog",
            table: "artworks",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(2048)",
            oldMaxLength: 2048,
            oldNullable: true);
    }
}
