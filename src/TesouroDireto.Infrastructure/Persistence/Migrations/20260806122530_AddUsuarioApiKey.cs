using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TesouroDireto.Infrastructure.Persistence.Migrations
{
    public partial class AddUsuarioApiKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    google_sub = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    nome = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    papel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    aprovado = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    aprovado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aprovado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.id);
                    table.ForeignKey(
                        name: "FK_usuarios_usuarios_aprovado_por",
                        column: x => x.aprovado_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prefixo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    dono_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revogada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_uso_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "FK_api_keys_usuarios_dono_usuario_id",
                        column: x => x.dono_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_dono_usuario_id",
                table: "api_keys",
                column: "dono_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_hash",
                table: "api_keys",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_aprovado_por",
                table: "usuarios",
                column: "aprovado_por");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_google_sub",
                table: "usuarios",
                column: "google_sub",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
