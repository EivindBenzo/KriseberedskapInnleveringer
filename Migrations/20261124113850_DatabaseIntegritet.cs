using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeimevernetInnlevering1.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseIntegritet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) ReservertAntall fikk feil standardverdi (1) i migrasjonen ResourceInventory. Ressurser som fantes
            //    da, ble stående som reservert uten at noen hadde bedt om dem. Rett bare de radene som har
            //    denne feilen: noe er reservert, men ingen forespørsel er ventende eller under arbeid.
            migrationBuilder.Sql("""
                UPDATE `Ressurs` r
                SET r.`ReservertAntall` = 0
                WHERE r.`ReservertAntall` > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM `Forespørsel` f
                      LEFT JOIN `Tildeling` t ON t.`ForespørselID` = f.`ForespørselID`
                      WHERE f.`RessursID` = r.`RessursID`
                        AND (f.`Svar` = 'venter' OR (f.`Svar` = 'godtatt' AND t.`Fullført` IS NULL)));
                """);

            // 2) Loggrader som peker på behov eller forespørsler som ikke finnes lenger, mister koblingen
            //    før fremmednøklene legges til. Ellers ville migrasjonen feilet på eksisterende databaser.
            migrationBuilder.Sql("""
                UPDATE `Hendelseslogg` l LEFT JOIN `Behov` b ON b.`BehovID` = l.`BehovID`
                SET l.`BehovID` = NULL WHERE l.`BehovID` IS NOT NULL AND b.`BehovID` IS NULL;
                """);
            migrationBuilder.Sql("""
                UPDATE `Hendelseslogg` l LEFT JOIN `Forespørsel` f ON f.`ForespørselID` = l.`ForespørselID`
                SET l.`ForespørselID` = NULL WHERE l.`ForespørselID` IS NOT NULL AND f.`ForespørselID` IS NULL;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "ReservertAntall",
                table: "Ressurs",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Verifisering_Resultat",
                table: "Verifisering",
                sql: "`Resultat` IN ('godkjent', 'avvist')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tildeling_Fremdrift",
                table: "Tildeling",
                sql: "`Fremdrift` IN ('bekreftet', 'på vei', 'fremme', 'fullført')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tildeling_LevertAntall",
                table: "Tildeling",
                sql: "`LevertAntall` IS NULL OR `LevertAntall` > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ressurs_Antall",
                table: "Ressurs",
                sql: "`Antall` >= 0 AND `ReservertAntall` >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ressurs_Koordinater",
                table: "Ressurs",
                sql: "`Breddegrad` BETWEEN -90 AND 90 AND `Lengdegrad` BETWEEN -180 AND 180");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ressurs_Periode",
                table: "Ressurs",
                sql: "`TilgjengeligTil` IS NULL OR `TilgjengeligTil` >= `TilgjengeligFra`");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Ressurs_Status",
                table: "Ressurs",
                sql: "`Status` IN ('venter på verifisering', 'tilgjengelig', 'forespurt', 'tildelt', 'i bruk', 'returnert', 'utilgjengelig', 'avvist')");

            migrationBuilder.CreateIndex(
                name: "IX_Hendelseslogg_BehovID",
                table: "Hendelseslogg",
                column: "BehovID");

            migrationBuilder.CreateIndex(
                name: "IX_Hendelseslogg_UtførtAv",
                table: "Hendelseslogg",
                column: "UtførtAv");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Hendelse_Koordinater",
                table: "Hendelse",
                sql: "(`Breddegrad` IS NULL OR `Breddegrad` BETWEEN -90 AND 90) AND (`Lengdegrad` IS NULL OR `Lengdegrad` BETWEEN -180 AND 180)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Hendelse_Prioritet",
                table: "Hendelse",
                sql: "`Prioritet` IN ('lav', 'medium', 'høy')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Hendelse_Status",
                table: "Hendelse",
                sql: "`Status` IN ('planlagt', 'aktiv', 'avsluttet')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Forespørsel_Antall",
                table: "Forespørsel",
                sql: "`Antall` > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Forespørsel_Svar",
                table: "Forespørsel",
                sql: "`Svar` IN ('venter', 'godtatt', 'avslått', 'avbrutt')");

            migrationBuilder.CreateIndex(
                name: "IX_Behovstype_Navn",
                table: "Behovstype",
                column: "Navn",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Behov_Antall",
                table: "Behov",
                sql: "`Antall` > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Behov_Koordinater",
                table: "Behov",
                sql: "`Breddegrad` BETWEEN -90 AND 90 AND `Lengdegrad` BETWEEN -180 AND 180");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Behov_Prioritet",
                table: "Behov",
                sql: "`Prioritet` IN ('akutt', 'planlagt')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Behov_Status",
                table: "Behov",
                sql: "`Status` IN ('ny', 'vurderes', 'tildelt', 'løst')");

            migrationBuilder.AddForeignKey(
                name: "FK_Hendelseslogg_Behov_BehovID",
                table: "Hendelseslogg",
                column: "BehovID",
                principalTable: "Behov",
                principalColumn: "BehovID",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Hendelseslogg_Bruker_UtførtAv",
                table: "Hendelseslogg",
                column: "UtførtAv",
                principalTable: "Bruker",
                principalColumn: "BrukerID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Hendelseslogg_Forespørsel_ForespørselID",
                table: "Hendelseslogg",
                column: "ForespørselID",
                principalTable: "Forespørsel",
                principalColumn: "ForespørselID",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Hendelseslogg_Behov_BehovID",
                table: "Hendelseslogg");

            migrationBuilder.DropForeignKey(
                name: "FK_Hendelseslogg_Bruker_UtførtAv",
                table: "Hendelseslogg");

            migrationBuilder.DropForeignKey(
                name: "FK_Hendelseslogg_Forespørsel_ForespørselID",
                table: "Hendelseslogg");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Verifisering_Resultat",
                table: "Verifisering");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tildeling_Fremdrift",
                table: "Tildeling");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tildeling_LevertAntall",
                table: "Tildeling");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ressurs_Antall",
                table: "Ressurs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ressurs_Koordinater",
                table: "Ressurs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ressurs_Periode",
                table: "Ressurs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Ressurs_Status",
                table: "Ressurs");

            migrationBuilder.DropIndex(
                name: "IX_Hendelseslogg_BehovID",
                table: "Hendelseslogg");

            migrationBuilder.DropIndex(
                name: "IX_Hendelseslogg_UtførtAv",
                table: "Hendelseslogg");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Hendelse_Koordinater",
                table: "Hendelse");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Hendelse_Prioritet",
                table: "Hendelse");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Hendelse_Status",
                table: "Hendelse");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Forespørsel_Antall",
                table: "Forespørsel");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Forespørsel_Svar",
                table: "Forespørsel");

            migrationBuilder.DropIndex(
                name: "IX_Behovstype_Navn",
                table: "Behovstype");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Behov_Antall",
                table: "Behov");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Behov_Koordinater",
                table: "Behov");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Behov_Prioritet",
                table: "Behov");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Behov_Status",
                table: "Behov");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReservertAntall",
                table: "Ressurs",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(12,2)",
                oldPrecision: 12,
                oldScale: 2,
                oldDefaultValue: 0m);
        }
    }
}
