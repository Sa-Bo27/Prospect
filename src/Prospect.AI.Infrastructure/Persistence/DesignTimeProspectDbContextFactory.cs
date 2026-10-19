using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Prospect.AI.Domain.Common;

namespace Prospect.AI.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef CLI to create migrations, without starting Aspire or a real database.
/// </summary>
public sealed class DesignTimeProspectDbContextFactory : IDesignTimeDbContextFactory<ProspectDbContext>
{
    public ProspectDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProspectDbContext>();
        ProspectDbContextOptions.Configure(options.UseNpgsql("Host=localhost;Database=prospectdb"));

        return new ProspectDbContext(options.Options, new NoCurrentUser());
    }

    private sealed class NoCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
    }
}
