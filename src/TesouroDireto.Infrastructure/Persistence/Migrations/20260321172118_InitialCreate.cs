using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TesouroDireto.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "titulos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_titulo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data_vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    indexador = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paga_juros_semestrais = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_titulos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tributos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    base_calculo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tipo_calculo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    cumulativo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tributos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "precos_taxas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_base = table.Column<DateOnly>(type: "date", nullable: false),
                    taxa_compra = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    taxa_venda = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    pu_compra = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    pu_venda = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    pu_base = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_precos_taxas", x => x.id);
                    table.ForeignKey(
                        name: "FK_precos_taxas_titulos_titulo_id",
                        column: x => x.titulo_id,
                        principalTable: "titulos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tributo_faixas",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dias_min = table.Column<int>(type: "integer", nullable: true),
                    dias_max = table.Column<int>(type: "integer", nullable: true),
                    dia = table.Column<int>(type: "integer", nullable: true),
                    aliquota = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    tributo_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tributo_faixas", x => x.id);
                    table.ForeignKey(
                        name: "FK_tributo_faixas_tributos_tributo_id",
                        column: x => x.tributo_id,
                        principalTable: "tributos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_precos_taxas_titulo_data",
                table: "precos_taxas",
                columns: new[] { "titulo_id", "data_base" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_titulos_tipo_vencimento",
                table: "titulos",
                columns: new[] { "tipo_titulo", "data_vencimento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tributo_faixas_tributo_id",
                table: "tributo_faixas",
                column: "tributo_id");

            migrationBuilder.CreateIndex(
                name: "ix_tributos_nome",
                table: "tributos",
                column: "nome",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "precos_taxas");

            migrationBuilder.DropTable(
                name: "tributo_faixas");

            migrationBuilder.DropTable(
                name: "titulos");

            migrationBuilder.DropTable(
                name: "tributos");
        }
    }
}
