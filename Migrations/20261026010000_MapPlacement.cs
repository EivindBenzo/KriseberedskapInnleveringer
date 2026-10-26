using HeimevernetInnlevering1.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeimevernetInnlevering1.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261026010000_MapPlacement")]
public class MapPlacement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "IsOnMap",
            table: "MapSubmissions",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "IsOnMap", table: "MapSubmissions");
}
