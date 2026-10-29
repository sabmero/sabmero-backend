using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using sabmero.Data;

#nullable disable

namespace sabmero.Migrations
{
    // Adds the two Settings-screen toggles to Users. Existing users default to ON.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261029120000_AddNotificationPreferences")]
    public partial class AddNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PromoAlertsEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PushEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromoAlertsEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PushEnabled",
                table: "Users");
        }
    }
}
