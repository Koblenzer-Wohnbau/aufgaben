using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aufgaben.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MailInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbound_mails",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TaskId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_mails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inbound_mails_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task_attachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_attachments_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attachment_blobs",
                columns: table => new
                {
                    AttachmentId = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachment_blobs", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_attachment_blobs_task_attachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "task_attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inbound_mails_MessageKey",
                table: "inbound_mails",
                column: "MessageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_mails_TaskId",
                table: "inbound_mails",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_task_attachments_TaskId",
                table: "task_attachments",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachment_blobs");

            migrationBuilder.DropTable(
                name: "inbound_mails");

            migrationBuilder.DropTable(
                name: "task_attachments");
        }
    }
}
