using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aufgaben.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WaitingAndSnooze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FollowUpNotifiedAt",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SnoozedUntil",
                table: "tasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingFor",
                table: "tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "WaitingSince",
                table: "tasks",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_SnoozedUntil",
                table: "tasks",
                column: "SnoozedUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tasks_SnoozedUntil",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "FollowUpNotifiedAt",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "SnoozedUntil",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "WaitingFor",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "WaitingSince",
                table: "tasks");
        }
    }
}
