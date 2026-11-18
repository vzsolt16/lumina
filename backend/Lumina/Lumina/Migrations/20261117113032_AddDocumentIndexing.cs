using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumina.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentIndexing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveIndexGeneration",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChunkingVersion",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IndexedContentVersion",
                table: "Documents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexingError",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexingStatus",
                table: "Documents",
                type: "TEXT",
                nullable: false,
                defaultValue: "NotIndexed");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastIndexedAt",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VectorDimensions",
                table: "Documents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_IndexingStatus",
                table: "Documents",
                column: "IndexingStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_IndexingStatus",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ActiveIndexGeneration",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ChunkingVersion",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IndexedContentVersion",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IndexingError",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IndexingStatus",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "LastIndexedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "VectorDimensions",
                table: "Documents");
        }
    }
}
