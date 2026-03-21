using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Infrastructure.Persistence.Configurations;

public sealed class TributoConfiguration : IEntityTypeConfiguration<Tributo>
{
    public void Configure(EntityTypeBuilder<Tributo> builder)
    {
        builder.ToTable("tributos");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.Nome)
            .HasColumnName("nome")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.BaseCalculo)
            .HasColumnName("base_calculo")
            .HasMaxLength(30)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(t => t.TipoCalculo)
            .HasColumnName("tipo_calculo")
            .HasMaxLength(30)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(t => t.Ativo)
            .HasColumnName("ativo")
            .IsRequired();

        builder.Property(t => t.Ordem)
            .HasColumnName("ordem")
            .IsRequired();

        builder.Property(t => t.Cumulativo)
            .HasColumnName("cumulativo")
            .IsRequired();

        builder.Navigation(t => t.Faixas)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(t => t.Faixas, faixaBuilder =>
        {
            faixaBuilder.ToTable("tributo_faixas");

            faixaBuilder.Property<int>("Id")
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            faixaBuilder.HasKey("Id");

            faixaBuilder.WithOwner()
                .HasForeignKey("TributoId");

            faixaBuilder.Property<Guid>("TributoId")
                .HasColumnName("tributo_id");

            faixaBuilder.Property(f => f.DiasMin)
                .HasColumnName("dias_min");

            faixaBuilder.Property(f => f.DiasMax)
                .HasColumnName("dias_max");

            faixaBuilder.Property(f => f.Dia)
                .HasColumnName("dia");

            faixaBuilder.Property(f => f.Aliquota)
                .HasColumnName("aliquota")
                .HasPrecision(8, 4)
                .IsRequired();
        });

        builder.HasIndex(t => t.Nome)
            .IsUnique()
            .HasDatabaseName("ix_tributos_nome");
    }
}
