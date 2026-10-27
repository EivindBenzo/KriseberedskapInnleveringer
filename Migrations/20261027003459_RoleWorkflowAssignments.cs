using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeimevernetInnlevering1.Migrations
{
    /// <inheritdoc />
    public partial class RoleWorkflowAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    NeedId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ActiveResourceId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedById = table.Column<string>(type: "varchar(127)", maxLength: 127, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyToken = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceAssignments_MapSubmissions_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "MapSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceAssignments_OperationalNeeds_NeedId",
                        column: x => x.NeedId,
                        principalTable: "OperationalNeeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceAssignments_ResourceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ResourceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAssignments_ActiveResourceId",
                table: "ResourceAssignments",
                column: "ActiveResourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAssignments_NeedId",
                table: "ResourceAssignments",
                column: "NeedId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAssignments_RequestId",
                table: "ResourceAssignments",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAssignments_ResourceId",
                table: "ResourceAssignments",
                column: "ResourceId");

            // Requests already allocated before assignments existed keep an active assignment.
            migrationBuilder.Sql(@"INSERT INTO `ResourceAssignments`
                (`RequestId`, `ResourceId`, `NeedId`, `Status`, `ActiveResourceId`, `CreatedAtUtc`, `CreatedById`, `ConcurrencyToken`)
                SELECT `Id`, `ResourceId`, `NeedId`, 0, `ResourceId`, UTC_TIMESTAMP(6), `CreatedById`, UUID()
                FROM `ResourceRequests` WHERE `Status` = 2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceAssignments");
        }
    }
}
