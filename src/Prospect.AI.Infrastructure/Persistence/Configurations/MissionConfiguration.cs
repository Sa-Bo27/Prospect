using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prospect.AI.Domain.Missions;
using Prospect.AI.Domain.Sources;

namespace Prospect.AI.Infrastructure.Persistence.Configurations;

internal sealed class MissionConfiguration : IEntityTypeConfiguration<Mission>
{
    public void Configure(EntityTypeBuilder<Mission> builder)
    {
        builder.HasKey(mission => mission.Id);
        builder.Property(mission => mission.Id).ValueGeneratedNever();

        builder.Property(mission => mission.Title).HasMaxLength(RawPosting.TitleMaxLength);
        builder.Property(mission => mission.Url).HasMaxLength(RawPosting.UrlMaxLength);
        builder.Property(mission => mission.CompanyName).HasMaxLength(Mission.CompanyNameMaxLength);
        builder.Property(mission => mission.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(mission => mission.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RawPosting>()
            .WithOne()
            .HasForeignKey<Mission>(mission => mission.RawPostingId)
            .OnDelete(DeleteBehavior.Cascade);

        // Missions list: "my missions to qualify, newest first".
        builder.HasIndex(mission => new { mission.OwnerId, mission.Status, mission.CreatedAt });
    }
}
