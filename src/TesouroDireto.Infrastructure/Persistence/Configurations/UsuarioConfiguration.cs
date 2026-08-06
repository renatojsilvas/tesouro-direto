using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Infrastructure.Persistence.Configurations;

public sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(u => u.GoogleSub)
            .HasColumnName("google_sub")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired()
            .HasConversion(
                e => e.Value,
                v => Email.Create(v).Value);

        builder.Property(u => u.Nome)
            .HasColumnName("nome")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(u => u.Papel)
            .HasColumnName("papel")
            .HasMaxLength(20)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(u => u.Aprovado)
            .HasColumnName("aprovado")
            .IsRequired();

        builder.Property(u => u.CriadoEm)
            .HasColumnName("criado_em")
            .IsRequired();

        builder.Property(u => u.AprovadoEm)
            .HasColumnName("aprovado_em")
            .IsRequired(false);

        builder.Property(u => u.AprovadoPor)
            .HasColumnName("aprovado_por")
            .IsRequired(false);

        builder.HasIndex(u => u.GoogleSub)
            .IsUnique()
            .HasDatabaseName("ix_usuarios_google_sub");

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("ix_usuarios_email");

        builder.HasIndex(u => u.AprovadoPor)
            .HasDatabaseName("ix_usuarios_aprovado_por");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(u => u.AprovadoPor)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
