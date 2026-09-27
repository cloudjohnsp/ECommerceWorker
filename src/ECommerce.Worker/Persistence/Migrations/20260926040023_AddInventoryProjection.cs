using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Worker.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_projections",
                schema: "worker",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableStock = table.Column<int>(type: "integer", nullable: false),
                    LastOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_projections", x => x.ProductId);
                    table.CheckConstraint("CK_inventory_projections_AvailableStock", "\"AvailableStock\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_projections_UpdatedAt",
                schema: "worker",
                table: "inventory_projections",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_projections",
                schema: "worker");
        }
    }
}
