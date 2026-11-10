using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumina.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredEditProposal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProposalContentVersion",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposalOperationJson",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposalContentVersion",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ProposalOperationJson",
                table: "ChatMessages");
        }
    }
}
