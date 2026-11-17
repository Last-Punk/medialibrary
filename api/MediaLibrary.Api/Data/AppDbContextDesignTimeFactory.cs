using MediaLibrary.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MediaLibrary.Api.Data;

/// <summary>
/// Creates an <see cref="AppDbContext"/> for EF tooling without starting the API host.
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string SqliteMigrationsAssembly = "MediaLibrary.Migrations.Sqlite";
    private const string PostgresMigrationsAssembly = "MediaLibrary.Migrations.Postgres";
    private const string SqliteFallbackConnectionString = "Data Source=medialibrary.db";
    private const string PostgresFallbackConnectionString =
        "Host=127.0.0.1;Port=1;Database=medialibrary_design_time;Username=design_time;Password=design_time";

    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environments.Production;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var configuredConnectionString = configuration.GetConnectionString("Default");
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        if (string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
                ? PostgresFallbackConnectionString
                : configuredConnectionString;

            optionsBuilder.UseNpgsql(
                connectionString,
                options => options.MigrationsAssembly(PostgresMigrationsAssembly));
        }
        else
        {
            var connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
                ? SqliteFallbackConnectionString
                : configuredConnectionString;

            optionsBuilder.UseSqlite(
                connectionString,
                options => options.MigrationsAssembly(SqliteMigrationsAssembly));
        }

        return new AppDbContext(optionsBuilder.Options);
    }
}
