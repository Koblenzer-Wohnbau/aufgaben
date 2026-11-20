using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aufgaben.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccessGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_grants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    ViewerId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedById = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedById = table.Column<int>(type: "integer", nullable: true),
                    ConsentNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedById = table.Column<int>(type: "integer", nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_access_grants_users_ConfirmedById",
                        column: x => x.ConfirmedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_grants_users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_grants_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_access_grants_users_RevokedById",
                        column: x => x.RevokedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_grants_users_ViewerId",
                        column: x => x.ViewerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_log",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GrantId = table.Column<int>(type: "integer", nullable: false),
                    ViewerId = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TaskId = table.Column<int>(type: "integer", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_log", x => x.Id);
                    table.ForeignKey(
                        name: "FK_access_log_access_grants_GrantId",
                        column: x => x.GrantId,
                        principalTable: "access_grants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_ConfirmedById",
                table: "access_grants",
                column: "ConfirmedById");

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_CreatedById",
                table: "access_grants",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_OwnerId_Status",
                table: "access_grants",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_RevokedById",
                table: "access_grants",
                column: "RevokedById");

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_ViewerId_Status",
                table: "access_grants",
                columns: new[] { "ViewerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_access_log_GrantId_At",
                table: "access_log",
                columns: new[] { "GrantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_access_log_OwnerId_At",
                table: "access_log",
                columns: new[] { "OwnerId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_log");

            migrationBuilder.DropTable(
                name: "access_grants");
        }
    }
}
