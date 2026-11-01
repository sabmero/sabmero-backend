using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using sabmero.Data;

#nullable disable

namespace sabmero.Migrations
{
    // Adds the optional Gender ("Male" / "Female") to Users. Idempotent.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261101140000_AddUserGender")]
    public partial class AddUserGender : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Users"
                    ADD COLUMN IF NOT EXISTS "Gender" character varying(10) NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "Gender";
                """);
        }
    }
}
