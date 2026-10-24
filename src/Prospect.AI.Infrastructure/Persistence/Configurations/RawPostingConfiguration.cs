using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prospect.AI.Domain.Missions;
using Prospect.AI.Domain.Sources;

namespace Prospect.AI.Infrastructure.Persistence.Configurations;

internal sealed class RawPostingConfiguration : IEntityTypeConfiguration<RawPosting>
{
    public void Configure(EntityTypeBuilder<RawPosting> builder)
    {
        builder.HasKey(posting => posting.Id);
        builder.Property(posting => posting.Id).ValueGeneratedNever();

        builder.Property(posting => posting.OriginalUrl).HasMaxLength(RawPosting.UrlMaxLength);
        builder.Property(posting => posting.CanonicalUrl).HasMaxLength(RawPosting.UrlMaxLength);
        builder.Property(posting => posting.ContentHash).HasMaxLength(RawPosting.ContentHashLength).IsFixedLength();
        builder.Property(posting => posting.Title).HasMaxLength(RawPosting.TitleMaxLength);

        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(posting => posting.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deduplication: a user never stores the same posting URL twice...
        builder.HasIndex(posting => new { posting.OwnerId, posting.CanonicalUrl }).IsUnique();
        // ...and identical content found under another URL is detected through its hash.
        builder.HasIndex(posting => new { posting.OwnerId, posting.ContentHash });
    }
}
