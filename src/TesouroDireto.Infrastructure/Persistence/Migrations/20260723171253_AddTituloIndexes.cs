using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TesouroDireto.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTituloIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_titulos_data_vencimento",
                table: "titulos",
                column: "data_vencimento");

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_titulos_nome_upper ON titulos (UPPER(tipo_titulo || ' ' || EXTRACT(YEAR FROM data_vencimento)::text));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_titulos_nome_upper;");

            migrationBuilder.DropIndex(
                name: "ix_titulos_data_vencimento",
                table: "titulos");
        }
    }
}
