using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Engagements.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class LifecycleCoordination : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ActivationRequested",
            schema: "engagements",
            table: "engagements",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "RequiresReservation",
            schema: "engagements",
            table: "engagements",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "lifecycle_operations",
            schema: "engagements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EngagementId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                PreviousState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                Error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_lifecycle_operations", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_lifecycle_operations_EngagementId_Kind",
            schema: "engagements",
            table: "lifecycle_operations",
            columns: new[] { "EngagementId", "Kind" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_lifecycle_operations_Status_NextAttemptAt",
            schema: "engagements",
            table: "lifecycle_operations",
            columns: new[] { "Status", "NextAttemptAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "lifecycle_operations",
            schema: "engagements");

        migrationBuilder.DropColumn(
            name: "ActivationRequested",
            schema: "engagements",
            table: "engagements");

        migrationBuilder.DropColumn(
            name: "RequiresReservation",
            schema: "engagements",
            table: "engagements");
    }
}
