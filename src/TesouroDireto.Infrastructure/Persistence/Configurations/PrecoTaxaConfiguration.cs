using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Infrastructure.Persistence.Configurations;

public sealed class PrecoTaxaConfiguration : IEntityTypeConfiguration<PrecoTaxa>
{
    public void Configure(EntityTypeBuilder<PrecoTaxa> builder)
    {
        builder.ToTable("precos_taxas");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(p => p.TituloId)
            .HasColumnName("titulo_id")
            .IsRequired();

        builder.Property(p => p.DataBase)
            .HasColumnName("data_base")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => DataBase.Create(v).Value);

        builder.Property(p => p.TaxaCompra)
            .HasColumnName("taxa_compra")
            .HasPrecision(10, 4)
            .IsRequired(false)
            .HasConversion(
                v => v != null ? v.Value : (decimal?)null,
                v => v.HasValue ? Taxa.Create(v.Value).Value : null);

        builder.Property(p => p.TaxaVenda)
            .HasColumnName("taxa_venda")
            .HasPrecision(10, 4)
            .IsRequired(false)
            .HasConversion(
                v => v != null ? v.Value : (decimal?)null,
                v => v.HasValue ? Taxa.Create(v.Value).Value : null);

        builder.Property(p => p.PuCompra)
            .HasColumnName("pu_compra")
            .HasPrecision(18, 6)
            .IsRequired(false)
            .HasConversion(
                v => v != null ? v.Value : (decimal?)null,
                v => v.HasValue ? PrecoUnitario.Create(v.Value).Value : null);

        builder.Property(p => p.PuVenda)
            .HasColumnName("pu_venda")
            .HasPrecision(18, 6)
            .IsRequired(false)
            .HasConversion(
                v => v != null ? v.Value : (decimal?)null,
                v => v.HasValue ? PrecoUnitario.Create(v.Value).Value : null);

        builder.Property(p => p.PuBase)
            .HasColumnName("pu_base")
            .HasPrecision(18, 6)
            .IsRequired(false)
            .HasConversion(
                v => v != null ? v.Value : (decimal?)null,
                v => v.HasValue ? PrecoUnitario.Create(v.Value).Value : null);

        builder.HasOne<TesouroDireto.Domain.Titulos.Titulo>()
            .WithMany()
            .HasForeignKey(p => p.TituloId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TituloId, p.DataBase })
            .IsUnique()
            .HasDatabaseName("ix_precos_taxas_titulo_data");
    }
}
