using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumina.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueFolderNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Atomic backstop for the "no duplicate folder names per level" rule.
            // Expression index because: COALESCE folds NULL ParentId (root level) to a
            // single bucket (SQLite treats NULLs as distinct otherwise), and NOCASE
            // makes it case-insensitive to match FolderService.EnsureNameAvailable.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Folders_UserId_Parent_Name\" " +
                "ON \"Folders\" (\"UserId\", COALESCE(\"ParentId\", ''), \"Name\" COLLATE NOCASE);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"IX_Folders_UserId_Parent_Name\";");
        }
    }
}
