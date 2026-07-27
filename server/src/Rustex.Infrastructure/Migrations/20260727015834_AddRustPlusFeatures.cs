using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rustex.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRustPlusFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RustPlusChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IsFromAssistant = table.Column<bool>(type: "boolean", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RustPlusChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RustPlusChatMessages_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RustPlusSmartDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LastKnownValue = table.Column<bool>(type: "boolean", nullable: true),
                    LastKnownCapacity = table.Column<int>(type: "integer", nullable: true),
                    LastKnownItemsJson = table.Column<string>(type: "text", nullable: true),
                    AlarmRaisesRaidEvent = table.Column<bool>(type: "boolean", nullable: false),
                    LastChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PairedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RustPlusSmartDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RustPlusSmartDevices_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RustPlusSmartDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RustPlusTeamMemberStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SteamId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsOnline = table.Column<bool>(type: "boolean", nullable: false),
                    IsAlive = table.Column<bool>(type: "boolean", nullable: false),
                    LastX = table.Column<float>(type: "real", nullable: false),
                    LastY = table.Column<float>(type: "real", nullable: false),
                    LastGrid = table.Column<string>(type: "text", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RustPlusTeamMemberStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RustPlusTeamMemberStates_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    ItemNameContains = table.Column<string>(type: "text", nullable: true),
                    MaxCostPerItem = table.Column<int>(type: "integer", nullable: true),
                    MinAmountInStock = table.Column<int>(type: "integer", nullable: false),
                    NotifyOnNewListing = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnPriceDrop = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnRestock = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    LastTriggeredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopAlerts_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShopAlerts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendingMachineSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkerId = table.Column<int>(type: "integer", nullable: false),
                    X = table.Column<float>(type: "real", nullable: false),
                    Y = table.Column<float>(type: "real", nullable: false),
                    Grid = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true),
                    OutOfStock = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendingMachineSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendingMachineSnapshots_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendingListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CurrencyId = table.Column<int>(type: "integer", nullable: false),
                    CostPerItem = table.Column<int>(type: "integer", nullable: false),
                    AmountInStock = table.Column<int>(type: "integer", nullable: false),
                    ItemIsBlueprint = table.Column<bool>(type: "boolean", nullable: false),
                    CurrencyIsBlueprint = table.Column<bool>(type: "boolean", nullable: false),
                    ItemCondition = table.Column<float>(type: "real", nullable: true),
                    ItemConditionMax = table.Column<float>(type: "real", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendingListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendingListings_VendingMachineSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "VendingMachineSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RustPlusChatMessages_ServerId_SentAt",
                table: "RustPlusChatMessages",
                columns: new[] { "ServerId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RustPlusSmartDevices_ServerId_EntityId",
                table: "RustPlusSmartDevices",
                columns: new[] { "ServerId", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RustPlusSmartDevices_UserId",
                table: "RustPlusSmartDevices",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RustPlusTeamMemberStates_ServerId_SteamId",
                table: "RustPlusTeamMemberStates",
                columns: new[] { "ServerId", "SteamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopAlerts_ServerId_IsEnabled",
                table: "ShopAlerts",
                columns: new[] { "ServerId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopAlerts_UserId",
                table: "ShopAlerts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VendingListings_SnapshotId_ItemId",
                table: "VendingListings",
                columns: new[] { "SnapshotId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendingMachineSnapshots_ServerId_MarkerId",
                table: "VendingMachineSnapshots",
                columns: new[] { "ServerId", "MarkerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RustPlusChatMessages");

            migrationBuilder.DropTable(
                name: "RustPlusSmartDevices");

            migrationBuilder.DropTable(
                name: "RustPlusTeamMemberStates");

            migrationBuilder.DropTable(
                name: "ShopAlerts");

            migrationBuilder.DropTable(
                name: "VendingListings");

            migrationBuilder.DropTable(
                name: "VendingMachineSnapshots");
        }
    }
}
