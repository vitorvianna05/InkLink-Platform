using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Registration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "artist_requested_at",
            schema: "identity",
            table: "profiles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "confirmed_email",
            schema: "identity",
            table: "profiles",
            type: "character varying(320)",
            maxLength: 320,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "email_confirmed_at",
            schema: "identity",
            table: "profiles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "phone_confirmed_at",
            schema: "identity",
            table: "profiles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "phone_number",
            schema: "identity",
            table: "profiles",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "registration_pending",
            schema: "identity",
            table: "profiles",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "terms_accepted_at",
            schema: "identity",
            table: "profiles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "terms_version",
            schema: "identity",
            table: "profiles",
            type: "character varying(60)",
            maxLength: 60,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "verification_codes",
            schema: "identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                target = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_verification_codes", x => x.id);
                table.CheckConstraint("ck_verification_codes_attempts", "attempts >= 0");
                table.ForeignKey(
                    name: "FK_verification_codes_profiles_profile_id",
                    column: x => x.profile_id,
                    principalSchema: "identity",
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_profiles_confirmed_phone",
            schema: "identity",
            table: "profiles",
            column: "phone_number",
            unique: true,
            filter: "phone_confirmed_at IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_verification_codes_profile_id_channel_created_at",
            schema: "identity",
            table: "verification_codes",
            columns: new[] { "profile_id", "channel", "created_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "verification_codes",
            schema: "identity");

        migrationBuilder.DropIndex(
            name: "ix_profiles_confirmed_phone",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "artist_requested_at",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "confirmed_email",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "email_confirmed_at",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "phone_confirmed_at",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "phone_number",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "registration_pending",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "terms_accepted_at",
            schema: "identity",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "terms_version",
            schema: "identity",
            table: "profiles");
    }
}
