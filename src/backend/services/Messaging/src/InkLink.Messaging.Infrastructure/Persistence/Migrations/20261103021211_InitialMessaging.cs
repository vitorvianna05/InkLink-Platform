using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InkLink.Messaging.Infrastructure.Persistence.Migrations; 
/// <inheritdoc />
public partial class InitialMessaging : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "messaging");

        migrationBuilder.CreateTable(
            name: "conversations",
            schema: "messaging",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EngagementId = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                ArtistAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                WritingBlocked = table.Column<bool>(type: "boolean", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_conversations", x => x.Id);
                table.CheckConstraint("ck_distinct_participants", "\"ClientId\" <> \"ArtistAccountId\"");
            });

        migrationBuilder.CreateTable(
            name: "inbox",
            schema: "messaging",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "messages",
            schema: "messaging",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_messages", x => x.Id);
                table.ForeignKey(
                    name: "FK_messages_conversations_ConversationId",
                    column: x => x.ConversationId,
                    principalSchema: "messaging",
                    principalTable: "conversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_conversations_ArtistAccountId_Id",
            schema: "messaging",
            table: "conversations",
            columns: new[] { "ArtistAccountId", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_conversations_ClientId_Id",
            schema: "messaging",
            table: "conversations",
            columns: new[] { "ClientId", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_conversations_EngagementId",
            schema: "messaging",
            table: "conversations",
            column: "EngagementId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_messages_ConversationId_AuthorId_RequestKey",
            schema: "messaging",
            table: "messages",
            columns: new[] { "ConversationId", "AuthorId", "RequestKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_messages_ConversationId_CreatedAt_Id",
            schema: "messaging",
            table: "messages",
            columns: new[] { "ConversationId", "CreatedAt", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox",
            schema: "messaging");

        migrationBuilder.DropTable(
            name: "messages",
            schema: "messaging");

        migrationBuilder.DropTable(
            name: "conversations",
            schema: "messaging");
    }
}
