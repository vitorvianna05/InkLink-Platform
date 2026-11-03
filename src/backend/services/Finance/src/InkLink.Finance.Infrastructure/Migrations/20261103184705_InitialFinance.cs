using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Finance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialFinance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "finance");

        migrationBuilder.CreateTable(
            name: "entries",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                EngagementId = table.Column<Guid>(type: "uuid", nullable: true),
                OriginalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_entries", x => x.Id);
                table.CheckConstraint("ck_positive_amount", "\"Amount\" > 0 AND \"Amount\" <= 9999999.99");
                table.ForeignKey(
                    name: "FK_entries_entries_OriginalEntryId",
                    column: x => x.OriginalEntryId,
                    principalSchema: "finance",
                    principalTable: "entries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_entries_ArtistId_EffectiveDate_Id",
            schema: "finance",
            table: "entries",
            columns: new[] { "ArtistId", "EffectiveDate", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_entries_ArtistId_RequestKey",
            schema: "finance",
            table: "entries",
            columns: new[] { "ArtistId", "RequestKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_entries_OriginalEntryId",
            schema: "finance",
            table: "entries",
            column: "OriginalEntryId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "entries",
            schema: "finance");
    }
}
