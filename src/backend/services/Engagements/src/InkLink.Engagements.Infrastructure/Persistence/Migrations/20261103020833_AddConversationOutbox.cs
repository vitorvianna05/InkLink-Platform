using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Engagements.Infrastructure.Persistence.Migrations; 
/// <inheritdoc />
public partial class AddConversationOutbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox",
            schema: "engagements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EngagementId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_EngagementId_Type",
            schema: "engagements",
            table: "outbox",
            columns: new[] { "EngagementId", "Type" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_PublishedAt_CreatedAt",
            schema: "engagements",
            table: "outbox",
            columns: new[] { "PublishedAt", "CreatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "outbox",
            schema: "engagements");
    }
}
