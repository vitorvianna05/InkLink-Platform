using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ProfilePhotos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "photo_key",
            schema: "identity",
            table: "profiles",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "photo_public",
            schema: "identity",
            table: "profiles",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "photo_revision",
            schema: "identity",
            table: "profiles",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "object_cleanups",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Private = table.Column<bool>(type: "boolean", nullable: false),
                DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_object_cleanups", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                QuarantinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_object_cleanups_DueAt",
            schema: "identity",
            table: "object_cleanups",
            column: "DueAt");

        migrationBuilder.CreateIndex(
            name: "IX_object_cleanups_Key",
            schema: "identity",
            table: "object_cleanups",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_PublishedAt_NextAttemptAt_CreatedAt",
            schema: "identity",
            table: "outbox",
            columns: new[] { "PublishedAt", "NextAttemptAt", "CreatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "object_cleanups",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "outbox",
            schema: "identity");

        migrationBuilder.DropColumn(
            name: "photo_key",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "photo_public",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "photo_revision",
            schema: "identity",
            table: "profiles");
    }
}
