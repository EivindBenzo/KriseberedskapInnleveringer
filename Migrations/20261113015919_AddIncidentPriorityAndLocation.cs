using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeimevernetInnlevering1.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentPriorityAndLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Breddegrad",
                table: "Hendelse",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Lengdegrad",
                table: "Hendelse",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Prioritet",
                table: "Hendelse",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "medium")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Breddegrad",
                table: "Hendelse");

            migrationBuilder.DropColumn(
                name: "Lengdegrad",
                table: "Hendelse");

            migrationBuilder.DropColumn(
                name: "Prioritet",
                table: "Hendelse");
        }
    }
}
