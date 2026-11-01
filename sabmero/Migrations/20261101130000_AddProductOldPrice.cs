using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using sabmero.Data;

#nullable disable

namespace sabmero.Migrations
{
    // Adds the optional "regular price" to products so the app can show a
    // strikethrough old price and a discount badge. Idempotent: safe even if
    // the column was already added by hand on the live database.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261101130000_AddProductOldPrice")]
    public partial class AddProductOldPrice : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Products"
                    ADD COLUMN IF NOT EXISTS "OldPrice" numeric(18,2) NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Products" DROP COLUMN IF EXISTS "OldPrice";
                """);
        }
    }
}
