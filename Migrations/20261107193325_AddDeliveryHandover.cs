using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeimevernetInnlevering1.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryHandover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Avvik",
                table: "Tildeling",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ForventetFremme",
                table: "Tildeling",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LevertAntall",
                table: "Tildeling",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mottakskontakt",
                table: "Tildeling",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "OverleveringBekreftet",
                table: "Tildeling",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Avvik",
                table: "Tildeling");

            migrationBuilder.DropColumn(
                name: "ForventetFremme",
                table: "Tildeling");

            migrationBuilder.DropColumn(
                name: "LevertAntall",
                table: "Tildeling");

            migrationBuilder.DropColumn(
                name: "Mottakskontakt",
                table: "Tildeling");

            migrationBuilder.DropColumn(
                name: "OverleveringBekreftet",
                table: "Tildeling");
        }
    }
}
