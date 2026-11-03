using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Engagements.Infrastructure.Persistence.Migrations; 
/// <inheritdoc />
public partial class OutboxRetrySchedule : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NextAttemptAt",
            schema: "engagements",
            table: "outbox",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "QuarantinedAt",
            schema: "engagements",
            table: "outbox",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "NextAttemptAt",
            schema: "engagements",
            table: "outbox");

        migrationBuilder.DropColumn(
            name: "QuarantinedAt",
            schema: "engagements",
            table: "outbox");
    }
}
