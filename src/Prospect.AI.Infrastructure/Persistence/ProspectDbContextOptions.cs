using Microsoft.EntityFrameworkCore;

namespace Prospect.AI.Infrastructure.Persistence;

/// <summary>
/// Options shared by the runtime registration and the design-time factory, so migrations match the app.
/// </summary>
internal static class ProspectDbContextOptions
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) =>
        options.UseSnakeCaseNamingConvention();
}
