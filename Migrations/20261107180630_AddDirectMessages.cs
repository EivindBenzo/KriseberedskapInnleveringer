using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeimevernetInnlevering1.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Melding",
                columns: table => new
                {
                    MeldingID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SendtAv = table.Column<int>(type: "int", nullable: false),
                    Mottaker = table.Column<int>(type: "int", nullable: false),
                    Emne = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Innhold = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Sendt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Lest = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Melding", x => x.MeldingID);
                    table.ForeignKey(
                        name: "FK_Melding_Bruker_Mottaker",
                        column: x => x.Mottaker,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Melding_Bruker_SendtAv",
                        column: x => x.SendtAv,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Melding_Mottaker_Lest_Sendt",
                table: "Melding",
                columns: new[] { "Mottaker", "Lest", "Sendt" });

            migrationBuilder.CreateIndex(
                name: "IX_Melding_SendtAv",
                table: "Melding",
                column: "SendtAv");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Melding");
        }
    }
}
