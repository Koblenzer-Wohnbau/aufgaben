using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aufgaben.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Trash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedById",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_DeletedAt",
                table: "tasks",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_DeletedById",
                table: "tasks",
                column: "DeletedById");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_users_DeletedById",
                table: "tasks",
                column: "DeletedById",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_users_DeletedById",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_DeletedAt",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_DeletedById",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "DeletedById",
                table: "tasks");
        }
    }
}
