using HeimevernetInnlevering1.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Xunit;

namespace MariaDbIntegrationTests;

public class DatabaseProviderConfigurationTests
{
    private const string Unused = "Server=unused;Database=unused;User=unused;Password=unused";

    [Fact]
    public void ConfiguredMySqlVersionIsUsedWithoutConnecting()
    {
        var version = DatabaseProviderConfiguration.ResolveServerVersion(Unused, "8.4.0-mysql");
        Assert.Equal(ServerType.MySql, version.Type);
        Assert.Equal(new Version(8, 4, 0), version.Version);
    }

    [Fact]
    public void ConfiguredMariaDbVersionIsUsedWithoutConnecting()
    {
        var version = DatabaseProviderConfiguration.ResolveServerVersion(Unused, "10.11.19-mariadb");
        Assert.Equal(ServerType.MariaDb, version.Type);
    }

    [Fact]
    public void InvalidConfiguredVersionGivesAClearError()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => DatabaseProviderConfiguration.ResolveServerVersion(Unused, "not-a-version"));
        Assert.Contains("DATABASE_SERVER_VERSION", error.Message);
    }

    [Fact]
    public void DetectedVersionMatchesTheRunningServerWhenNothingIsConfigured()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connection = DatabaseProviderConfiguration.GetRequiredConnectionString(configuration);
        var detected = DatabaseProviderConfiguration.ResolveServerVersion(connection, null);
        Assert.True(detected.Type is ServerType.MariaDb or ServerType.MySql);
        Assert.Same(detected, DatabaseProviderConfiguration.ResolveServerVersion(connection, ""));
    }
}
