using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aufgaben.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SharedLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_list_members",
                columns: table => new
                {
                    ListId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_list_members", x => new { x.ListId, x.UserId });
                    table.ForeignKey(
                        name: "FK_task_list_members_task_lists_ListId",
                        column: x => x.ListId,
                        principalTable: "task_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_list_members_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_list_members_UserId",
                table: "task_list_members",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_list_members");
        }
    }
}
