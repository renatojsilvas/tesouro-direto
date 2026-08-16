using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TesouroDireto.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrecoTaxaDataBaseIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_precos_taxas_data_base",
                table: "precos_taxas",
                column: "data_base");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_precos_taxas_data_base",
                table: "precos_taxas");
        }
    }
}
