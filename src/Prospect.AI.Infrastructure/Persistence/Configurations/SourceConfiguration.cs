using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prospect.AI.Domain.Sources;

namespace Prospect.AI.Infrastructure.Persistence.Configurations;

internal sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.HasKey(source => source.Id);
        builder.Property(source => source.Id).ValueGeneratedNever();

        builder.Property(source => source.Name).HasMaxLength(Source.NameMaxLength);
        builder.Property(source => source.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(source => source.Url).HasMaxLength(Source.UrlMaxLength);

        builder.HasIndex(source => source.OwnerId);
    }
}
