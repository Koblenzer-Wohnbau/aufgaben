using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aufgaben.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ManagedFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InboxViewEnabled",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "feature_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    AbsencesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ImportedInboxUsers = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_settings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_settings");

            migrationBuilder.DropColumn(
                name: "InboxViewEnabled",
                table: "users");
        }
    }
}
