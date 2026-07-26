using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rustex.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiProviderAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DiscordUsername",
                table: "Users",
                newName: "Username");

            migrationBuilder.RenameColumn(
                name: "DiscordAvatar",
                table: "Users",
                newName: "SteamId");

            migrationBuilder.AlterColumn<string>(
                name: "DiscordId",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_SteamId",
                table: "Users",
                column: "SteamId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_SteamId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "Username",
                table: "Users",
                newName: "DiscordUsername");

            migrationBuilder.RenameColumn(
                name: "SteamId",
                table: "Users",
                newName: "DiscordAvatar");

            migrationBuilder.AlterColumn<string>(
                name: "DiscordId",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
