using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Scheduling.Infrastructure.Migrations;

/// <inheritdoc />
public partial class CompletedSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "completed_sessions",
            schema: "scheduling",
            columns: table => new
            {
                ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                EngagementId = table.Column<Guid>(type: "uuid", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_completed_sessions", x => x.ReservationId);
                table.CheckConstraint("ck_session_amount", "\"Amount\" > 0 AND \"Amount\" <= 9999999.99");
                table.ForeignKey(
                    name: "FK_completed_sessions_reservations_ReservationId",
                    column: x => x.ReservationId,
                    principalSchema: "scheduling",
                    principalTable: "reservations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_completed_sessions_ArtistId_CompletedAt",
            schema: "scheduling",
            table: "completed_sessions",
            columns: new[] { "ArtistId", "CompletedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "completed_sessions",
            schema: "scheduling");
    }
}
