using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InkLink.Bff.Migrations;

/// <inheritdoc />
public partial class InitialBffSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "bff");

        migrationBuilder.CreateTable(
            name: "DataProtectionKeys",
            schema: "bff",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FriendlyName = table.Column<string>(type: "text", nullable: true),
                Xml = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Sessions",
            schema: "bff",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                ProtectedTicket = table.Column<byte[]>(type: "bytea", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Sessions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Sessions_ExpiresAt",
            schema: "bff",
            table: "Sessions",
            column: "ExpiresAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DataProtectionKeys",
            schema: "bff");

        migrationBuilder.DropTable(
            name: "Sessions",
            schema: "bff");
    }
}
