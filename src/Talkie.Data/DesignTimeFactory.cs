using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Talkie.Data;

/// <summary>Used by <c>dotnet ef migrations add</c>; no database connection is needed to create migrations.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<TalkieDbContext>
{
    public TalkieDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TalkieDbContext>()
            .UseSqlServer("Server=localhost;Database=talkiedb;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.UseCompatibilityLevel(170))
            .Options);
}
