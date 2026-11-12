using System.Globalization;
using ClearCase.Application.Disputes;
using ClearCase.Application.Transactions;
using ClearCase.Infrastructure.Disputes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClearCase.Infrastructure.Transactions;

public static class SqlitePersistence
{
    public static IServiceCollection AddTransactionPersistence(
        this IServiceCollection services, string contentRoot, string? webRoot, bool development)
    {
        services.AddDbContext<TransactionDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var connectionString = CreateConnectionString(configuration, contentRoot, webRoot, development, out var timeout);
            options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(timeout));
        });
        services.AddScoped<ITransactionReader, SqliteTransactionReader>();
        services.AddScoped<IDisputeStore, SqliteDisputeStore>();
        return services;
    }

    private static string CreateConnectionString(
        IConfiguration configuration, string contentRoot, string? webRoot, bool development, out int timeout)
    {
        var configuredPath = configuration["Persistence:DatabasePath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("Persistence:DatabasePath is required.");
        if (!int.TryParse(configuration["Persistence:BusyTimeoutSeconds"],
                NumberStyles.None, CultureInfo.InvariantCulture, out timeout) || timeout <= 0)
            throw new InvalidOperationException("Persistence:BusyTimeoutSeconds must be a positive integer.");

        var path = Path.GetFullPath(configuredPath, contentRoot);
        var staticDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(webRoot ?? Path.Combine(contentRoot, "wwwroot"))) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.StartsWith(staticDirectory, comparison))
            throw new InvalidOperationException("The SQLite database must not be stored in the web root.");

        if (development)
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = development ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            DefaultTimeout = timeout,
            Pooling = false
        }.ToString();
    }
}
