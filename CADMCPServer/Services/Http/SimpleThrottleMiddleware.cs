using System.Collections.Concurrent;
using CADMCPServer.Configuration;
using Microsoft.Extensions.Options;

namespace CADMCPServer.Services.Http;

public sealed class SimpleThrottleMiddleware
{
    private static readonly ConcurrentDictionary<string, CounterWindow> Windows = new(StringComparer.OrdinalIgnoreCase);

    private readonly RequestDelegate _next;
    private readonly ThrottleSettings _settings;

    public SimpleThrottleMiddleware(RequestDelegate next, IOptions<ThrottleSettings> settings)
    {
        _next = next;
        _settings = settings.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_settings.Enabled || _settings.PermitLimitPerMinute <= 0)
        {
            await _next(context);
            return;
        }

        var key = BuildClientKey(context);
        var now = DateTimeOffset.UtcNow;

        var entry = Windows.AddOrUpdate(
            key,
            _ => new CounterWindow(now, 1),
            (_, existing) => existing.ShouldReset(now)
                ? new CounterWindow(now, 1)
                : existing.Increment());

        if (entry.Count > _settings.PermitLimitPerMinute)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "rate_limit_exceeded",
                message = "Too many requests. Please retry shortly."
            });
            return;
        }

        await _next(context);
    }

    private static string BuildClientKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"{ip}:{context.Request.Path.Value}";
    }

    private readonly record struct CounterWindow(DateTimeOffset Start, int Count)
    {
        public bool ShouldReset(DateTimeOffset now) => now - Start >= TimeSpan.FromMinutes(1);
        public CounterWindow Increment() => this with { Count = Count + 1 };
    }
}
