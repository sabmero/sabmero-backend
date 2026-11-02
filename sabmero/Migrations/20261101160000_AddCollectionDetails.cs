using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using sabmero.Data;

#nullable disable

namespace sabmero.Migrations
{
    // Cash / online collection details on service bookings and orders (rider),
    // plus the payment method on technician/rider payments.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261101160000_AddCollectionDetails")]
    public partial class AddCollectionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CollectionAmountInWords",
                table: "ServiceBookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollectionMethod",
                table: "ServiceBookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollectionScreenshotPath",
                table: "ServiceBookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollectionAmountInWords",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollectionMethod",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollectionScreenshotPath",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "TechnicianPayments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Cash");

            migrationBuilder.AddColumn<string>(
                name: "AmountInWords",
                table: "TechnicianPayments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CollectionAmountInWords", table: "ServiceBookings");
            migrationBuilder.DropColumn(name: "CollectionMethod", table: "ServiceBookings");
            migrationBuilder.DropColumn(name: "CollectionScreenshotPath", table: "ServiceBookings");
            migrationBuilder.DropColumn(name: "CollectionAmountInWords", table: "Orders");
            migrationBuilder.DropColumn(name: "CollectionMethod", table: "Orders");
            migrationBuilder.DropColumn(name: "CollectionScreenshotPath", table: "Orders");
            migrationBuilder.DropColumn(name: "DeliveredAt", table: "Orders");
            migrationBuilder.DropColumn(name: "Method", table: "TechnicianPayments");
            migrationBuilder.DropColumn(name: "AmountInWords", table: "TechnicianPayments");
        }
    }
}
