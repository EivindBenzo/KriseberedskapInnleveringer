using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeimevernetInnlevering1.Migrations
{
    /// <inheritdoc />
    public partial class ResourceInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Antall",
                table: "Ressurs",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "Enhet",
                table: "Ressurs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "stk")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ErForbruksvare",
                table: "Ressurs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ReservertAntall",
                table: "Ressurs",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "Antall",
                table: "Forespørsel",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Antall",
                table: "Ressurs");

            migrationBuilder.DropColumn(
                name: "Enhet",
                table: "Ressurs");

            migrationBuilder.DropColumn(
                name: "ErForbruksvare",
                table: "Ressurs");

            migrationBuilder.DropColumn(
                name: "ReservertAntall",
                table: "Ressurs");

            migrationBuilder.DropColumn(
                name: "Antall",
                table: "Forespørsel");
        }
    }
}
