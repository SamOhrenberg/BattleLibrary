using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace BattleLibrary.Api.Hosting;

public static class HostingExtensions
{
    public const string AuthPolicy = "auth";
    public const string SearchPolicy = "search";
    public const string ImportPolicy = "import";
    public const string FeedbackPolicy = "feedback";

    // Console always; Axiom (OTLP over HTTP) when Axiom:Token is set.
    public static void AddStructuredLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, logger) =>
        {
            logger
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "battle-library-api")
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);

            var token = context.Configuration["Axiom:Token"];
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var dataset = context.Configuration["Axiom:Dataset"] ?? "battle-library";
            logger.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = "https://api.axiom.co/v1/logs";
                options.Protocol = OtlpProtocol.HttpProtobuf;
                options.Headers = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {token}",
                    ["X-Axiom-Dataset"] = dataset,
                };
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = "battle-library-api",
                    ["deployment.environment"] = context.HostingEnvironment.EnvironmentName,
                };
            });
        });
    }

    public static void AddErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
            };
        });
        services.AddExceptionHandler<UnhandledExceptionHandler>();
    }

    public static void AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, ct) =>
            {
                var http = ctx.HttpContext;
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiting")
                    .LogWarning("Rate limit hit on {Method} {Path} by {Client}", http.Request.Method, http.Request.Path, ClientKey(http));

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await http.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "You are going too fast. Wait a moment and try again.",
                }, ct);
            };

            // Credential endpoints: per IP, strict.
            options.AddPolicy(AuthPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                $"auth:{IpKey(http)}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.AddPolicy(SearchPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                $"search:{ClientKey(http)}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.AddPolicy(ImportPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                $"import:{ClientKey(http)}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // Each submission opens a public GitHub issue.
            options.AddPolicy(FeedbackPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                $"feedback:{ClientKey(http)}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));

            // Everything else: generous per-user/IP cap.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"global:{ClientKey(http)}",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    // Railway terminates TLS and proxies; trust its forwarded headers so the
    // client IP (rate limiting, logs) is the real one.
    public static void AddForwardedHeaders(this IServiceCollection services) =>
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

    // Adds user id and client IP to each request's log event.
    public static void UseApiRequestLogging(this WebApplication app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (http, _, ex) =>
                ex is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
                : http.Request.Path.StartsWithSegments("/api/health") ? LogEventLevel.Verbose
                : http.Response.StatusCode >= 400 ? LogEventLevel.Warning
                : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnostic, http) =>
            {
                diagnostic.Set("ClientIp", IpKey(http));
                if (http.User.FindFirstValue("sub") is { } userId)
                {
                    diagnostic.Set("UserId", userId);
                }
            };
        });

    public static bool FixedTimeEquals(string? a, string? b) =>
        a is not null && b is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string IpKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string ClientKey(HttpContext http) =>
        http.User.FindFirstValue("sub") is { } sub ? $"user:{sub}" : $"ip:{IpKey(http)}";
}

internal sealed class UnhandledExceptionHandler(IProblemDetailsService problems, ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        // Client went away mid-request; not a server fault.
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        logger.LogError(exception, "Unhandled exception on {Method} {Path}", http.Request.Method, http.Request.Path);

        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Something went wrong",
                Detail = "An unexpected error occurred. If it keeps happening, quote the trace id.",
            },
        });
    }
}
