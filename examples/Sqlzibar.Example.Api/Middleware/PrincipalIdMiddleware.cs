namespace Sqlzibar.Example.Api.Middleware;

public class PrincipalIdMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] SkipPaths = ["/swagger", "/sqlzibar", "/"];

    public PrincipalIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip middleware for non-API paths
        if (path == "/" || SkipPaths.Any(p => p != "/" && path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Principal-Id", out var principalId) ||
            string.IsNullOrWhiteSpace(principalId))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "X-Principal-Id header is required" });
            return;
        }

        context.Items["PrincipalId"] = principalId.ToString();
        await _next(context);
    }
}

public static class PrincipalIdMiddlewareExtensions
{
    public static IApplicationBuilder UsePrincipalIdMiddleware(this IApplicationBuilder app)
        => app.UseMiddleware<PrincipalIdMiddleware>();

    public static string GetPrincipalId(this HttpContext context)
        => context.Items["PrincipalId"] as string
           ?? throw new InvalidOperationException("PrincipalId not found. Ensure PrincipalIdMiddleware is registered.");
}
