using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sqlzibar.Configuration;
using Sqlzibar.Interfaces;

namespace Sqlzibar.Services;

public class SqlzibarSchemaInitializer
{
    private const int CurrentSchemaVersion = 1;

    private readonly ISqlzibarDbContext _context;
    private readonly SqlzibarOptions _options;
    private readonly ILogger<SqlzibarSchemaInitializer> _logger;

    public SqlzibarSchemaInitializer(
        ISqlzibarDbContext context,
        IOptions<SqlzibarOptions> options,
        ILogger<SqlzibarSchemaInitializer> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking Sqlzibar schema version...");

        var schema = _options.Schema;

        // Ensure the version tracking table exists
        var ensureVersionTableSql = $@"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SqlzibarSchema' AND schema_id = SCHEMA_ID('{schema}'))
BEGIN
    CREATE TABLE [{schema}].[SqlzibarSchema] ([Version] INT NOT NULL);
END";
        await _context.Database.ExecuteSqlRawAsync(ensureVersionTableSql, cancellationToken);

        // Read the current version
        var connection = _context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
            await connection.OpenAsync(cancellationToken);

        int? currentVersion = null;
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT TOP 1 [Version] FROM [{schema}].[SqlzibarSchema]";
            if (_context.Database.CurrentTransaction != null)
                cmd.Transaction = _context.Database.CurrentTransaction.GetDbTransaction();

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result != null && result != DBNull.Value)
                currentVersion = Convert.ToInt32(result);
        }
        finally
        {
            if (!wasOpen)
                await connection.CloseAsync();
        }

        if (currentVersion == null)
        {
            _logger.LogInformation("Fresh install detected. Running initial schema creation (v{Version})...", CurrentSchemaVersion);
            await RunScriptAsync("Sqlzibar.Schema.001_Initial.sql", cancellationToken);
            _logger.LogInformation("Schema v{Version} installed successfully.", CurrentSchemaVersion);
        }
        else if (currentVersion < CurrentSchemaVersion)
        {
            _logger.LogInformation("Schema upgrade needed: v{Current} -> v{Target}", currentVersion, CurrentSchemaVersion);

            // Future upgrade scripts would be executed here in order:
            // if (currentVersion < 2) await RunScriptAsync("Sqlzibar.Schema.002_UpgradeName.sql", ct);
            // if (currentVersion < 3) await RunScriptAsync("Sqlzibar.Schema.003_UpgradeName.sql", ct);

            _logger.LogInformation("Schema upgraded to v{Version}.", CurrentSchemaVersion);
        }
        else
        {
            _logger.LogInformation("Schema is up to date (v{Version}).", currentVersion);
        }
    }

    private async Task RunScriptAsync(string resourceName, CancellationToken cancellationToken)
    {
        var assembly = typeof(SqlzibarSchemaInitializer).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

        using var reader = new StreamReader(stream);
        var rawSql = await reader.ReadToEndAsync(cancellationToken);

        // Replace placeholders with configured values
        var sql = SubstitutePlaceholders(rawSql);

        // Split on GO batches (GO on its own line)
        var batches = Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToArray();

        _logger.LogDebug("Executing {Count} SQL batch(es) from {Resource}...", batches.Length, resourceName);

        foreach (var batch in batches)
        {
            await _context.Database.ExecuteSqlRawAsync(batch, cancellationToken);
        }
    }

    private string SubstitutePlaceholders(string sql)
    {
        var tables = _options.TableNames;

        return sql
            .Replace("{Schema}", _options.Schema)
            .Replace("{PrincipalTypes}", tables.PrincipalTypes)
            .Replace("{Principals}", tables.Principals)
            .Replace("{UserGroups}", tables.UserGroups)
            .Replace("{UserGroupMemberships}", tables.UserGroupMemberships)
            .Replace("{ResourceTypes}", tables.ResourceTypes)
            .Replace("{Resources}", tables.Resources)
            .Replace("{Grants}", tables.Grants)
            .Replace("{Roles}", tables.Roles)
            .Replace("{Permissions}", tables.Permissions)
            .Replace("{RolePermissions}", tables.RolePermissions)
            .Replace("{ServiceAccounts}", tables.ServiceAccounts);
    }
}
