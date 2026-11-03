using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Engagements.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ReservationReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "reservation_inbox",
            schema: "engagements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reservation_inbox", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "reservation_receipts",
            schema: "engagements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EngagementId = table.Column<Guid>(type: "uuid", nullable: false),
                Payload = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reservation_receipts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_reservation_receipts_EngagementId",
            schema: "engagements",
            table: "reservation_receipts",
            column: "EngagementId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "reservation_inbox",
            schema: "engagements");

        migrationBuilder.DropTable(
            name: "reservation_receipts",
            schema: "engagements");
    }
}
