using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HeimevernetInnlevering1.Migrations
{
    /// <inheritdoc />
    public partial class ErModell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Behovstype",
                columns: table => new
                {
                    BehovstypeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Navn = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Behovstype", x => x.BehovstypeID);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Bruker",
                columns: table => new
                {
                    BrukerID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Fornavn = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Etternavn = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Organisasjon = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Epost = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmailConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PassordHash = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SecurityStamp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Telefon = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumberConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bruker", x => x.BrukerID);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Hendelseslogg",
                columns: table => new
                {
                    LoggID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RessursID = table.Column<int>(type: "int", nullable: true),
                    BehovID = table.Column<int>(type: "int", nullable: true),
                    ForespørselID = table.Column<int>(type: "int", nullable: true),
                    UtførtAv = table.Column<int>(type: "int", nullable: false),
                    Tidspunkt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Handling = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Begrunnelse = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hendelseslogg", x => x.LoggID);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Ressurstype",
                columns: table => new
                {
                    RessurstypeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Navn = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ressurstype", x => x.RessurstypeID);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Rolle",
                columns: table => new
                {
                    RolleID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Navn = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rolle", x => x.RolleID);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BrukerClaim",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrukerClaim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrukerClaim_Bruker_UserId",
                        column: x => x.UserId,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BrukerInnlogging",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderKey = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderDisplayName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrukerInnlogging", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_BrukerInnlogging_Bruker_UserId",
                        column: x => x.UserId,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BrukerToken",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrukerToken", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_BrukerToken_Bruker_UserId",
                        column: x => x.UserId,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Hendelse",
                columns: table => new
                {
                    HendelseID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OpprettetAv = table.Column<int>(type: "int", nullable: false),
                    Navn = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Beskrivelse = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Område = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Starttidspunkt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hendelse", x => x.HendelseID);
                    table.ForeignKey(
                        name: "FK_Hendelse_Bruker_OpprettetAv",
                        column: x => x.OpprettetAv,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BehovstypeRessurstype",
                columns: table => new
                {
                    BehovstypeID = table.Column<int>(type: "int", nullable: false),
                    RessurstypeID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BehovstypeRessurstype", x => new { x.BehovstypeID, x.RessurstypeID });
                    table.ForeignKey(
                        name: "FK_BehovstypeRessurstype_Behovstype_BehovstypeID",
                        column: x => x.BehovstypeID,
                        principalTable: "Behovstype",
                        principalColumn: "BehovstypeID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BehovstypeRessurstype_Ressurstype_RessurstypeID",
                        column: x => x.RessurstypeID,
                        principalTable: "Ressurstype",
                        principalColumn: "RessurstypeID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Ressurs",
                columns: table => new
                {
                    RessursID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RessurstypeID = table.Column<int>(type: "int", nullable: false),
                    EierID = table.Column<int>(type: "int", nullable: false),
                    Navn = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Beskrivelse = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TilgjengeligFra = table.Column<DateOnly>(type: "date", nullable: false),
                    TilgjengeligTil = table.Column<DateOnly>(type: "date", nullable: true),
                    Kontaktperson = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kontakttelefon = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Breddegrad = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Lengdegrad = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Stedsnavn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Registrert = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ressurs", x => x.RessursID);
                    table.ForeignKey(
                        name: "FK_Ressurs_Bruker_EierID",
                        column: x => x.EierID,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ressurs_Ressurstype_RessurstypeID",
                        column: x => x.RessurstypeID,
                        principalTable: "Ressurstype",
                        principalColumn: "RessurstypeID",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BrukerRolle",
                columns: table => new
                {
                    BrukerID = table.Column<int>(type: "int", nullable: false),
                    RolleID = table.Column<int>(type: "int", nullable: false),
                    Tildelt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrukerRolle", x => new { x.BrukerID, x.RolleID });
                    table.ForeignKey(
                        name: "FK_BrukerRolle_Bruker_BrukerID",
                        column: x => x.BrukerID,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BrukerRolle_Rolle_RolleID",
                        column: x => x.RolleID,
                        principalTable: "Rolle",
                        principalColumn: "RolleID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RolleClaim",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolleClaim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolleClaim_Rolle_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Rolle",
                        principalColumn: "RolleID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Behov",
                columns: table => new
                {
                    BehovID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BehovstypeID = table.Column<int>(type: "int", nullable: false),
                    RegistrertAv = table.Column<int>(type: "int", nullable: false),
                    HendelseID = table.Column<int>(type: "int", nullable: true),
                    Beskrivelse = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Prioritet = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Antall = table.Column<int>(type: "int", nullable: false),
                    Frist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Kontaktperson = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kontakttelefon = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Breddegrad = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Lengdegrad = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Stedsnavn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Registrert = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Behov", x => x.BehovID);
                    table.ForeignKey(
                        name: "FK_Behov_Behovstype_BehovstypeID",
                        column: x => x.BehovstypeID,
                        principalTable: "Behovstype",
                        principalColumn: "BehovstypeID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Behov_Bruker_RegistrertAv",
                        column: x => x.RegistrertAv,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Behov_Hendelse_HendelseID",
                        column: x => x.HendelseID,
                        principalTable: "Hendelse",
                        principalColumn: "HendelseID",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Verifisering",
                columns: table => new
                {
                    VerifiseringID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RessursID = table.Column<int>(type: "int", nullable: false),
                    VerifisertAv = table.Column<int>(type: "int", nullable: false),
                    Tidspunkt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Resultat = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Begrunnelse = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verifisering", x => x.VerifiseringID);
                    table.ForeignKey(
                        name: "FK_Verifisering_Bruker_VerifisertAv",
                        column: x => x.VerifisertAv,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Verifisering_Ressurs_RessursID",
                        column: x => x.RessursID,
                        principalTable: "Ressurs",
                        principalColumn: "RessursID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Forespørsel",
                columns: table => new
                {
                    ForespørselID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BehovID = table.Column<int>(type: "int", nullable: false),
                    RessursID = table.Column<int>(type: "int", nullable: false),
                    SendtAv = table.Column<int>(type: "int", nullable: false),
                    Sendt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Svar = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Svart = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Forespørsel", x => x.ForespørselID);
                    table.ForeignKey(
                        name: "FK_Forespørsel_Behov_BehovID",
                        column: x => x.BehovID,
                        principalTable: "Behov",
                        principalColumn: "BehovID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Forespørsel_Bruker_SendtAv",
                        column: x => x.SendtAv,
                        principalTable: "Bruker",
                        principalColumn: "BrukerID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Forespørsel_Ressurs_RessursID",
                        column: x => x.RessursID,
                        principalTable: "Ressurs",
                        principalColumn: "RessursID",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Tildeling",
                columns: table => new
                {
                    TildelingID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ForespørselID = table.Column<int>(type: "int", nullable: false),
                    Tildelt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Fremdrift = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SistOppdatert = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Fullført = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tildeling", x => x.TildelingID);
                    table.ForeignKey(
                        name: "FK_Tildeling_Forespørsel_ForespørselID",
                        column: x => x.ForespørselID,
                        principalTable: "Forespørsel",
                        principalColumn: "ForespørselID",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "Behovstype",
                columns: new[] { "BehovstypeID", "Navn" },
                values: new object[,]
                {
                    { 1, "Transport" },
                    { 2, "Strøm" },
                    { 3, "Søk og overvåking" },
                    { 4, "Mannskap" },
                    { 5, "Graving og rydding" },
                    { 6, "Utstyr" }
                });

            migrationBuilder.InsertData(
                table: "Ressurstype",
                columns: new[] { "RessurstypeID", "Navn" },
                values: new object[,]
                {
                    { 1, "Kjøretøy" },
                    { 2, "Maskin" },
                    { 3, "Drone" },
                    { 4, "Generator" },
                    { 5, "Personell" },
                    { 6, "Utstyr" }
                });

            migrationBuilder.InsertData(
                table: "BehovstypeRessurstype",
                columns: new[] { "BehovstypeID", "RessurstypeID" },
                values: new object[,]
                {
                    { 1, 1 },
                    { 2, 4 },
                    { 3, 3 },
                    { 3, 5 },
                    { 4, 5 },
                    { 5, 2 },
                    { 6, 6 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Behov_BehovstypeID",
                table: "Behov",
                column: "BehovstypeID");

            migrationBuilder.CreateIndex(
                name: "IX_Behov_HendelseID",
                table: "Behov",
                column: "HendelseID");

            migrationBuilder.CreateIndex(
                name: "IX_Behov_RegistrertAv",
                table: "Behov",
                column: "RegistrertAv");

            migrationBuilder.CreateIndex(
                name: "IX_BehovstypeRessurstype_RessurstypeID",
                table: "BehovstypeRessurstype",
                column: "RessurstypeID");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Bruker",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Bruker",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrukerClaim_UserId",
                table: "BrukerClaim",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrukerInnlogging_UserId",
                table: "BrukerInnlogging",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrukerRolle_RolleID",
                table: "BrukerRolle",
                column: "RolleID");

            migrationBuilder.CreateIndex(
                name: "IX_Forespørsel_BehovID",
                table: "Forespørsel",
                column: "BehovID");

            migrationBuilder.CreateIndex(
                name: "IX_Forespørsel_RessursID_Svar",
                table: "Forespørsel",
                columns: new[] { "RessursID", "Svar" });

            migrationBuilder.CreateIndex(
                name: "IX_Forespørsel_SendtAv",
                table: "Forespørsel",
                column: "SendtAv");

            migrationBuilder.CreateIndex(
                name: "IX_Hendelse_OpprettetAv",
                table: "Hendelse",
                column: "OpprettetAv");

            migrationBuilder.CreateIndex(
                name: "IX_Hendelseslogg_ForespørselID",
                table: "Hendelseslogg",
                column: "ForespørselID");

            migrationBuilder.CreateIndex(
                name: "IX_Hendelseslogg_RessursID_Tidspunkt",
                table: "Hendelseslogg",
                columns: new[] { "RessursID", "Tidspunkt" });

            migrationBuilder.CreateIndex(
                name: "IX_Ressurs_EierID",
                table: "Ressurs",
                column: "EierID");

            migrationBuilder.CreateIndex(
                name: "IX_Ressurs_RessurstypeID",
                table: "Ressurs",
                column: "RessurstypeID");

            migrationBuilder.CreateIndex(
                name: "IX_Ressurs_Status",
                table: "Ressurs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Ressurstype_Navn",
                table: "Ressurstype",
                column: "Navn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Rolle",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolleClaim_RoleId",
                table: "RolleClaim",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Tildeling_ForespørselID",
                table: "Tildeling",
                column: "ForespørselID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Verifisering_RessursID_Tidspunkt",
                table: "Verifisering",
                columns: new[] { "RessursID", "Tidspunkt" });

            migrationBuilder.CreateIndex(
                name: "IX_Verifisering_VerifisertAv",
                table: "Verifisering",
                column: "VerifisertAv");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BehovstypeRessurstype");

            migrationBuilder.DropTable(
                name: "BrukerClaim");

            migrationBuilder.DropTable(
                name: "BrukerInnlogging");

            migrationBuilder.DropTable(
                name: "BrukerRolle");

            migrationBuilder.DropTable(
                name: "BrukerToken");

            migrationBuilder.DropTable(
                name: "Hendelseslogg");

            migrationBuilder.DropTable(
                name: "RolleClaim");

            migrationBuilder.DropTable(
                name: "Tildeling");

            migrationBuilder.DropTable(
                name: "Verifisering");

            migrationBuilder.DropTable(
                name: "Rolle");

            migrationBuilder.DropTable(
                name: "Forespørsel");

            migrationBuilder.DropTable(
                name: "Behov");

            migrationBuilder.DropTable(
                name: "Ressurs");

            migrationBuilder.DropTable(
                name: "Behovstype");

            migrationBuilder.DropTable(
                name: "Hendelse");

            migrationBuilder.DropTable(
                name: "Ressurstype");

            migrationBuilder.DropTable(
                name: "Bruker");
        }
    }
}
