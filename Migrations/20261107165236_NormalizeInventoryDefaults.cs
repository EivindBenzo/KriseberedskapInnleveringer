using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeimevernetInnlevering1.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeInventoryDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE `Ressurs` SET `Antall` = 1, `Enhet` = 'stk' WHERE `Antall` = 0 OR `Enhet` = '';");
            migrationBuilder.Sql("UPDATE `Forespørsel` SET `Antall` = 1 WHERE `Antall` = 0;");
            migrationBuilder.Sql("ALTER TABLE `Ressurs` ALTER `Antall` SET DEFAULT 1;");
            migrationBuilder.Sql("ALTER TABLE `Ressurs` ALTER `Enhet` SET DEFAULT 'stk';");
            migrationBuilder.Sql("ALTER TABLE `Forespørsel` ALTER `Antall` SET DEFAULT 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `Ressurs` ALTER `Antall` SET DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `Ressurs` ALTER `Enhet` SET DEFAULT '';");
            migrationBuilder.Sql("ALTER TABLE `Forespørsel` ALTER `Antall` SET DEFAULT 0;");
        }
    }
}
