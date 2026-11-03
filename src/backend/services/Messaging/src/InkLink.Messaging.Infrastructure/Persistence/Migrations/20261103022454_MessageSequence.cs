using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Messaging.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class MessageSequence : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_messages_ConversationId_CreatedAt_Id",
            schema: "messaging",
            table: "messages");

        migrationBuilder.AddColumn<long>(
            name: "Sequence",
            schema: "messaging",
            table: "messages",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "LastMessageSequence",
            schema: "messaging",
            table: "conversations",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.Sql("""
            WITH ranked AS (
                SELECT "Id", row_number() OVER (PARTITION BY "ConversationId" ORDER BY "CreatedAt", "Id") AS sequence
                FROM messaging.messages
            )
            UPDATE messaging.messages AS message SET "Sequence" = ranked.sequence
            FROM ranked WHERE message."Id" = ranked."Id";
            UPDATE messaging.conversations AS conversation SET "LastMessageSequence" = totals.last_sequence
            FROM (SELECT "ConversationId", max("Sequence") AS last_sequence FROM messaging.messages GROUP BY "ConversationId") AS totals
            WHERE conversation."Id" = totals."ConversationId";
            """);
        migrationBuilder.CreateIndex(
            name: "IX_messages_ConversationId_Sequence",
            schema: "messaging",
            table: "messages",
            columns: new[] { "ConversationId", "Sequence" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_messages_ConversationId_Sequence",
            schema: "messaging",
            table: "messages");

        migrationBuilder.DropColumn(
            name: "Sequence",
            schema: "messaging",
            table: "messages");

        migrationBuilder.DropColumn(
            name: "LastMessageSequence",
            schema: "messaging",
            table: "conversations");

        migrationBuilder.CreateIndex(
            name: "IX_messages_ConversationId_CreatedAt_Id",
            schema: "messaging",
            table: "messages",
            columns: new[] { "ConversationId", "CreatedAt", "Id" });
    }
}
