using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rustex.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build a context without starting the API.
///
/// Without this, EF falls back to running Rustex.Api's Program.cs to find the context — which now
/// throws when <c>Encryption:FieldKey</c> is unset, so generating a migration would require the
/// production secrets to be present. Migrations only need a connection string shaped correctly
/// enough to pick the Npgsql provider; they never open the connection to scaffold.</summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=rustex;Username=rustex;Password=changeme";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
