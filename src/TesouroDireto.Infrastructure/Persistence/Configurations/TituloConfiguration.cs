using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesouroDireto.Domain.Titulos;

namespace TesouroDireto.Infrastructure.Persistence.Configurations;

public sealed class TituloConfiguration : IEntityTypeConfiguration<Titulo>
{
    public void Configure(EntityTypeBuilder<Titulo> builder)
    {
        builder.ToTable("titulos");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.TipoTitulo)
            .HasColumnName("tipo_titulo")
            .HasMaxLength(100)
            .IsRequired()
            .HasConversion(
                v => v.Name,
                v => TipoTitulo.FromName(v).Value);

        builder.Property(t => t.DataVencimento)
            .HasColumnName("data_vencimento")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => DataVencimento.Create(v).Value);

        builder.Property(t => t.Indexador)
            .HasColumnName("indexador")
            .HasMaxLength(20)
            .IsRequired()
            .HasConversion(
                v => v.Name,
                v => Indexador.FromName(v).Value);

        builder.Property(t => t.PagaJurosSemestrais)
            .HasColumnName("paga_juros_semestrais")
            .IsRequired();

        builder.HasIndex(t => new { t.TipoTitulo, t.DataVencimento })
            .IsUnique()
            .HasDatabaseName("ix_titulos_tipo_vencimento");
    }
}
