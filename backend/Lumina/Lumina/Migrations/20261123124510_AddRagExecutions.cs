using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumina.Migrations
{
    /// <inheritdoc />
    public partial class AddRagExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RagExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Query = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: true),
                    DocumentVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    RetrievalDurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    GenerationDurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    RetrievedChunkCount = table.Column<int>(type: "INTEGER", nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    Answer = table.Column<string>(type: "TEXT", nullable: true),
                    ContextJson = table.Column<string>(type: "TEXT", nullable: true),
                    FailureStage = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorType = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RagExecutions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RagExecutions_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RagExecutions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RagExecutions_ConversationId",
                table: "RagExecutions",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_RagExecutions_DocumentId",
                table: "RagExecutions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RagExecutions_UserId_ConversationId_StartedAt",
                table: "RagExecutions",
                columns: new[] { "UserId", "ConversationId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RagExecutions_UserId_StartedAt",
                table: "RagExecutions",
                columns: new[] { "UserId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RagExecutions");
        }
    }
}
