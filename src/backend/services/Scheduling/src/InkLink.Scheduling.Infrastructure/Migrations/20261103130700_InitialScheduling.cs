using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Scheduling.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialScheduling : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

        migrationBuilder.EnsureSchema(
            name: "scheduling");

        migrationBuilder.CreateTable(
            name: "calendars",
            schema: "scheduling",
            columns: table => new
            {
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_calendars", x => x.ArtistId);
            });

        migrationBuilder.CreateTable(
            name: "outbox",
            schema: "scheduling",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
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

        migrationBuilder.CreateTable(
            name: "availability",
            schema: "scheduling",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                End = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_availability", x => x.Id);
                table.CheckConstraint("ck_availability_interval", "\"End\" > \"Start\"");
                table.ForeignKey(
                    name: "FK_availability_calendars_ArtistId",
                    column: x => x.ArtistId,
                    principalSchema: "scheduling",
                    principalTable: "calendars",
                    principalColumn: "ArtistId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "reservations",
            schema: "scheduling",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                EngagementId = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ClientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ArtistName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Project = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                ReferenceAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reservations", x => x.Id);
                table.CheckConstraint("ck_reservation_interval", "\"End\" > \"Start\" AND \"ArtistId\" <> \"ClientId\"");
                table.ForeignKey(
                    name: "FK_reservations_calendars_ArtistId",
                    column: x => x.ArtistId,
                    principalSchema: "scheduling",
                    principalTable: "calendars",
                    principalColumn: "ArtistId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_availability_ArtistId_Date",
            schema: "scheduling",
            table: "availability",
            columns: new[] { "ArtistId", "Date" });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_PublishedAt_QuarantinedAt_NextAttemptAt",
            schema: "scheduling",
            table: "outbox",
            columns: new[] { "PublishedAt", "QuarantinedAt", "NextAttemptAt" });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_ReservationId",
            schema: "scheduling",
            table: "outbox",
            column: "ReservationId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_reservations_ArtistId_LocalDate_Start_Id",
            schema: "scheduling",
            table: "reservations",
            columns: new[] { "ArtistId", "LocalDate", "Start", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_reservations_ArtistId_OperationId",
            schema: "scheduling",
            table: "reservations",
            columns: new[] { "ArtistId", "OperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_reservations_ClientId_LocalDate_Start_Id",
            schema: "scheduling",
            table: "reservations",
            columns: new[] { "ClientId", "LocalDate", "Start", "Id" });
        migrationBuilder.Sql("""
            ALTER TABLE scheduling.reservations
            ADD CONSTRAINT ex_artist_time
            EXCLUDE USING gist ("ArtistId" WITH =, tstzrange("Start", "End", '[)') WITH &&);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "availability",
            schema: "scheduling");

        migrationBuilder.DropTable(
            name: "outbox",
            schema: "scheduling");

        migrationBuilder.DropTable(
            name: "reservations",
            schema: "scheduling");

        migrationBuilder.DropTable(
            name: "calendars",
            schema: "scheduling");
    }
}
