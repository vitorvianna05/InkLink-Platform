using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Engagements.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialEngagements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "engagements");

        migrationBuilder.CreateTable(
            name: "engagements",
            schema: "engagements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                FlashId = table.Column<Guid>(type: "uuid", nullable: true),
                Idea = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                BodyRegion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                ApproximateSizeCm = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                CatalogSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CurrentProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_engagements", x => x.Id);
                table.CheckConstraint("ck_distinct_participants", "\"ClientId\" <> \"ArtistAccountId\"");
                table.CheckConstraint("ck_size", "\"ApproximateSizeCm\" > 0 AND \"ApproximateSizeCm\" <= 300");
            });

        migrationBuilder.CreateTable(
            name: "proposals",
            schema: "engagements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EngagementId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_proposals", x => x.Id);
                table.CheckConstraint("ck_amount", "\"Amount\" > 0");
                table.ForeignKey(
                    name: "FK_proposals_engagements_EngagementId",
                    column: x => x.EngagementId,
                    principalSchema: "engagements",
                    principalTable: "engagements",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_engagements_ArtistAccountId_Id",
            schema: "engagements",
            table: "engagements",
            columns: new[] { "ArtistAccountId", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_engagements_ClientId_Id",
            schema: "engagements",
            table: "engagements",
            columns: new[] { "ClientId", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_engagements_ClientId_RequestKey",
            schema: "engagements",
            table: "engagements",
            columns: new[] { "ClientId", "RequestKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_proposals_EngagementId_RequestKey",
            schema: "engagements",
            table: "proposals",
            columns: new[] { "EngagementId", "RequestKey" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "proposals",
            schema: "engagements");

        migrationBuilder.DropTable(
            name: "engagements",
            schema: "engagements");
    }
}
