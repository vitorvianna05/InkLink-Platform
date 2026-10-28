using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class LinkExternalAccounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_artist",
            schema: "identity",
            table: "profiles",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "external_accounts",
            schema: "identity",
            columns: table => new
            {
                issuer = table.Column<string>(type: "text", nullable: false),
                subject = table.Column<string>(type: "text", nullable: false),
                profile_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_external_accounts", x => new { x.issuer, x.subject });
                table.ForeignKey(
                    name: "FK_external_accounts_profiles_profile_id",
                    column: x => x.profile_id,
                    principalSchema: "identity",
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_external_accounts_profile_id",
            schema: "identity",
            table: "external_accounts",
            column: "profile_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "external_accounts",
            schema: "identity");

        migrationBuilder.DropColumn(
            name: "is_artist",
            schema: "identity",
            table: "profiles");
    }
}
