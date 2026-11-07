using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeimevernetInnlevering1.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentDecisionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HendelseID",
                table: "Hendelseslogg",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hendelseslogg_HendelseID_Tidspunkt",
                table: "Hendelseslogg",
                columns: new[] { "HendelseID", "Tidspunkt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Hendelseslogg_Hendelse_HendelseID",
                table: "Hendelseslogg",
                column: "HendelseID",
                principalTable: "Hendelse",
                principalColumn: "HendelseID",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Hendelseslogg_Hendelse_HendelseID",
                table: "Hendelseslogg");

            migrationBuilder.DropIndex(
                name: "IX_Hendelseslogg_HendelseID_Tidspunkt",
                table: "Hendelseslogg");

            migrationBuilder.DropColumn(
                name: "HendelseID",
                table: "Hendelseslogg");
        }
    }
}
