using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Messaging.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PermanentClosure : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "Closed",
            schema: "messaging",
            table: "conversations",
            type: "boolean",
            nullable: false,
            defaultValue: false);
        migrationBuilder.Sql("UPDATE messaging.conversations SET \"Closed\" = true WHERE \"WritingBlocked\" = true");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Closed",
            schema: "messaging",
            table: "conversations");
    }
}
