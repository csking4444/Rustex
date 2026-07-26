using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rustex.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRustPlusPairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RustPlusPairings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    PlayerTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    ServerIp = table.Column<string>(type: "text", nullable: false),
                    ServerPort = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RustPlusPairings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RustPlusPairings_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RustPlusPairings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RustPlusPairings_ServerId",
                table: "RustPlusPairings",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_RustPlusPairings_UserId_ServerId",
                table: "RustPlusPairings",
                columns: new[] { "UserId", "ServerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RustPlusPairings");
        }
    }
}
