using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace HeimevernetInnlevering1.Data;

public static class DatabaseProviderConfiguration
{
    public static readonly ServerVersion MariaDbServerVersion =
        new MariaDbServerVersion(new Version(10, 11, 19));

    // Detection opens a connection, so do it once per connection string rather than for every DbContext.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ServerVersion> DetectedVersions = new();

    public static string GetRequiredConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        try
        {
            var parsed = string.IsNullOrWhiteSpace(connectionString)
                ? new MySqlConnectionStringBuilder()
                : new MySqlConnectionStringBuilder(connectionString);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                var port = configuration["MARIADB_PORT"];
                if (!uint.TryParse(port, out var parsedPort) || parsedPort == 0)
                {
                    throw new InvalidOperationException(
                        "MARIADB_PORT must be a valid MariaDB port when using component-based configuration.");
                }

                parsed.Server = Required(configuration["MARIADB_HOST"], "MARIADB_HOST");
                parsed.Port = parsedPort;
                parsed.Database = Required(configuration["MARIADB_DATABASE"], "MARIADB_DATABASE");
                parsed.UserID = Required(configuration["MARIADB_USER"], "MARIADB_USER");
                parsed.Password = Required(
                    configuration["MARIADB_APP_PASSWORD"],
                    "MARIADB_APP_PASSWORD");
            }

            if (string.IsNullOrWhiteSpace(parsed.Server)
                || string.IsNullOrWhiteSpace(parsed.Database)
                || string.IsNullOrWhiteSpace(parsed.UserID)
                || string.IsNullOrWhiteSpace(parsed.Password))
            {
                throw new InvalidOperationException(
                    "The MariaDB connection string must include a server, database, user, and password.");
            }

            return parsed.ConnectionString;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                "The configured MariaDB connection values are invalid.");
        }
    }

    private static string Required(string? value, string setting)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{setting} must be configured for MariaDB.")
            : value;
    }

    /// <summary>
    /// Uses <paramref name="serverVersion"/> (for example "8.4.0-mysql" or "10.11.19-mariadb") when given,
    /// otherwise detects the version from the server so both MariaDB and MySQL 8 work.
    /// </summary>
    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        string connectionString,
        string? serverVersion = null)
    {
        return options.UseMySql(connectionString, ResolveServerVersion(connectionString, serverVersion));
    }

    public static ServerVersion ResolveServerVersion(string connectionString, string? serverVersion)
    {
        if (string.IsNullOrWhiteSpace(serverVersion))
            return DetectedVersions.GetOrAdd(connectionString, ServerVersion.AutoDetect);
        try
        {
            return ServerVersion.Parse(serverVersion.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "DATABASE_SERVER_VERSION is invalid. Use for example 8.4.0-mysql or 10.11.19-mariadb.");
        }
    }
}
