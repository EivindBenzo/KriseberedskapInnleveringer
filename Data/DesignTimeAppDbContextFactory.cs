using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HeimevernetInnlevering1.Data;

public class DesignTimeAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        var connectionString =
            DatabaseProviderConfiguration.GetRequiredConnectionString(configuration);
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProviderConfiguration.Configure(options, connectionString, configuration["DATABASE_SERVER_VERSION"]);

        return new AppDbContext(options.Options);
    }
}
