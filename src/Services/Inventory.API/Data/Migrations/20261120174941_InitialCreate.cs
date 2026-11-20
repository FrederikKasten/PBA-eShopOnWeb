using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Inventory.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "StockItems",
                columns: table => new
                {
                    CatalogItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    OnHand = table.Column<int>(type: "INTEGER", nullable: false),
                    Reserved = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItems", x => x.CatalogItemId);
                });

            migrationBuilder.CreateTable(
                name: "ReservationLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    CatalogItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Units = table.Column<int>(type: "INTEGER", nullable: false),
                    AvailableAtCheck = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationLine_Reservations_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Reservations",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "StockItems",
                columns: new[] { "CatalogItemId", "OnHand", "Reserved", "Version" },
                values: new object[,]
                {
                    { 1, 20, 0, 0 },
                    { 2, 20, 0, 0 },
                    { 3, 20, 0, 0 },
                    { 4, 20, 0, 0 },
                    { 5, 2, 0, 0 },
                    { 6, 20, 0, 0 },
                    { 7, 20, 0, 0 },
                    { 8, 20, 0, 0 },
                    { 9, 20, 0, 0 },
                    { 10, 20, 0, 0 },
                    { 11, 20, 0, 0 },
                    { 12, 20, 0, 0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLine_OrderId",
                table: "ReservationLine",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationLine");

            migrationBuilder.DropTable(
                name: "StockItems");

            migrationBuilder.DropTable(
                name: "Reservations");
        }
    }
}
