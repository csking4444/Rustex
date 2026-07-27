using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rustex.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRustPlusAccountCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FacepunchServerId",
                table: "RustServers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RustPlusAccountCredentials",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SteamId = table.Column<string>(type: "text", nullable: true),
                    CredentialsEncrypted = table.Column<string>(type: "text", nullable: false),
                    PersistentIdsJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastNotificationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RustPlusAccountCredentials", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_RustPlusAccountCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RustPlusLinkCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedFromIp = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RustPlusLinkCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RustPlusLinkCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RustPlusLinkCodes_CodeHash",
                table: "RustPlusLinkCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RustPlusLinkCodes_UserId",
                table: "RustPlusLinkCodes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RustPlusAccountCredentials");

            migrationBuilder.DropTable(
                name: "RustPlusLinkCodes");

            migrationBuilder.DropColumn(
                name: "FacepunchServerId",
                table: "RustServers");
        }
    }
}
