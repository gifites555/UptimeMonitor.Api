using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UptimeMonitor.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MonitorId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_incidents_monitors_MonitorId",
                        column: x => x.MonitorId,
                        principalTable: "monitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_MonitorId_ResolvedAtUtc",
                table: "incidents",
                columns: new[] { "MonitorId", "ResolvedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidents");
        }
    }
}
