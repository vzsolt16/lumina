using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumina.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredDocumentContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentJson",
                table: "Documents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentJson",
                table: "Documents");
        }
    }
}
