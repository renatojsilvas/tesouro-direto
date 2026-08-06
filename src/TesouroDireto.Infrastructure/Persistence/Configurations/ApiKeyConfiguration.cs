using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Infrastructure.Persistence.Configurations;

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(k => k.Nome)
            .HasColumnName("nome")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(k => k.Hash)
            .HasColumnName("hash")
            .HasMaxLength(64)
            .IsRequired()
            .HasConversion(
                h => h.Value,
                v => ApiKeyHash.Create(v).Value);

        builder.Property(k => k.Prefixo)
            .HasColumnName("prefixo")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(k => k.DonoUsuarioId)
            .HasColumnName("dono_usuario_id")
            .IsRequired();

        builder.Property(k => k.Ativa)
            .HasColumnName("ativa")
            .IsRequired();

        builder.Property(k => k.CriadaEm)
            .HasColumnName("criada_em")
            .IsRequired();

        builder.Property(k => k.RevogadaEm)
            .HasColumnName("revogada_em")
            .IsRequired(false);

        builder.Property(k => k.UltimoUsoEm)
            .HasColumnName("ultimo_uso_em")
            .IsRequired(false);

        builder.HasIndex(k => k.Hash)
            .IsUnique()
            .HasDatabaseName("ix_api_keys_hash");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(k => k.DonoUsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(k => k.DonoUsuarioId)
            .HasDatabaseName("ix_api_keys_dono_usuario_id");
    }
}
