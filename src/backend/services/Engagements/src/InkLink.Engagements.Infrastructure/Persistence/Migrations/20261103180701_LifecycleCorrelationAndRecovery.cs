using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Engagements.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class LifecycleCorrelationAndRecovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CorrelationId",
            schema: "engagements",
            table: "lifecycle_operations",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TraceParent",
            schema: "engagements",
            table: "lifecycle_operations",
            type: "text",
            nullable: true);
        migrationBuilder.Sql("""
            UPDATE engagements.lifecycle_operations AS op
            SET "Status" = 'Pending', "Attempts" = 0, "Error" = NULL, "NextAttemptAt" = NULL
            FROM engagements.engagements AS e
            WHERE op."EngagementId" = e."Id"
              AND op."Kind" = 'Activate' AND op."Status" = 'Rejected'
              AND op."Error" = 'engagements.closing'
              AND e."RequiresReservation" AND e."ActivationRequested"
              AND e."State" IN ('Agreed', 'Closing');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CorrelationId",
            schema: "engagements",
            table: "lifecycle_operations");

        migrationBuilder.DropColumn(
            name: "TraceParent",
            schema: "engagements",
            table: "lifecycle_operations");
    }
}
