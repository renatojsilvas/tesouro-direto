using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesouroDireto.Domain.Feriados;

namespace TesouroDireto.Infrastructure.Persistence.Configurations;

public sealed class FeriadoConfiguration : IEntityTypeConfiguration<Feriado>
{
    public void Configure(EntityTypeBuilder<Feriado> builder)
    {
        builder.ToTable("feriados");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(f => f.Data)
            .HasColumnName("data")
            .HasConversion(
                v => v.Value,
                v => DataFeriado.Create(v).Value)
            .IsRequired();

        builder.Property(f => f.Descricao)
            .HasColumnName("descricao")
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(f => f.Data)
            .IsUnique()
            .HasDatabaseName("ix_feriados_data");
    }
}
