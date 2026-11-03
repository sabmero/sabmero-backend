using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using sabmero.Data;

#nullable disable

namespace sabmero.Migrations
{
    // Staff hand-over of collected money to SabMero (+ staff payout QR).
    [DbContext(typeof(AppDbContext))]
    [Migration("20261103120000_AddStaffRemittances")]
    public partial class AddStaffRemittances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentQrPath",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StaffRemittances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StaffId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "Cash"),
                    AmountInWords = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ScreenshotPath = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "Pending"),
                    RejectReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedByUserId = table.Column<int>(type: "integer", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffRemittances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffRemittances_Users_StaffId",
                        column: x => x.StaffId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffRemittances_StaffId_Status",
                table: "StaffRemittances",
                columns: new[] { "StaffId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StaffRemittances");
            migrationBuilder.DropColumn(name: "PaymentQrPath", table: "Users");
        }
    }
}
