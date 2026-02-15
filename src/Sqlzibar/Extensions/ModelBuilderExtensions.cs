using Microsoft.EntityFrameworkCore;
using Sqlzibar.Configuration;
using Sqlzibar.Interfaces;

namespace Sqlzibar.Extensions;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies the Sqlzibar entity model configuration and registers the TVF.
    /// Call this from your DbContext's OnModelCreating method.
    /// Pass GetType() so the TVF can be registered on the correct DbContext type.
    /// </summary>
    /// <example>
    /// modelBuilder.ApplySqlzibarModel(GetType());
    /// </example>
    public static ModelBuilder ApplySqlzibarModel(
        this ModelBuilder modelBuilder,
        Type contextType,
        Action<SqlzibarOptions>? configure = null)
    {
        var options = new SqlzibarOptions();
        configure?.Invoke(options);

        SqlzibarModelConfiguration.Configure(modelBuilder, options, contextType);

        return modelBuilder;
    }

    /// <summary>
    /// Applies the Sqlzibar entity model configuration WITHOUT TVF registration.
    /// Use this for InMemory/unit test contexts where TVFs are not supported.
    /// </summary>
    public static ModelBuilder ApplySqlzibarModel(
        this ModelBuilder modelBuilder,
        Action<SqlzibarOptions>? configure = null)
    {
        var options = new SqlzibarOptions();
        configure?.Invoke(options);

        SqlzibarModelConfiguration.Configure(modelBuilder, options, contextType: null);

        return modelBuilder;
    }
}
