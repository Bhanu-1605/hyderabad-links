using System.Reflection;
using System.Threading.RateLimiting;
using BusinessDirectory.Api.Auth;
using BusinessDirectory.Api.Data;
using BusinessDirectory.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// `dotnet run --project backend/BusinessDirectory.Api -- migrate` applies migrations and seeds, then exits.
if (args.Contains("migrate"))
{
    Environment.Exit(MigrationRunner.Run(builder.Configuration, builder.Environment.ContentRootPath));
}

// `-- seed-dev` loads clearly labelled sample data. It refuses to run outside the Development environment.
if (args.Contains("seed-dev"))
{
    Environment.Exit(MigrationRunner.SeedDev(builder.Configuration, builder.Environment.ContentRootPath, builder.Environment.IsDevelopment()));
}

// `-- create-admin --email ... --name ... [--role admin|super_admin]` creates the first admin account.
// There is deliberately no HTTP endpoint for this (docs/security.md).
if (args.Contains("create-admin"))
{
    Environment.Exit(AdminBootstrap.Run(builder.Configuration, args));
}

// ---- Configuration -------------------------------------------------------------------
// Everything deployment-specific (origins, limits, later: connection strings) comes from
// configuration / environment variables. Nothing environment-specific is hardcoded here.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var globalPermitPerMinute = builder.Configuration.GetValue("RateLimiting:GlobalPermitPerMinute", 300);

// Fail fast on an unsafe production setup instead of silently running with it.
if (!builder.Environment.IsDevelopment() && allowedOrigins.Any(o => o.Trim() == "*"))
{
    throw new InvalidOperationException("Cors:AllowedOrigins must not contain '*' outside Development.");
}

// ---- Services ------------------------------------------------------------------------
builder.Services.AddProblemDetails();

// Missing or unparseable request parameters must answer 400 in every environment.
// (Development otherwise throws, and the global error handler would turn that into a 500.)
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

// Database: registered only when a connection string exists. Raw SQL through Npgsql, no ORM (decision D6).
var connectionString = builder.Configuration.GetConnectionString("Default");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
}
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// Deny-by-default CORS: with no configured origins, no cross-origin browser access is granted.
// (Browsers talk to their own Next.js server, which calls this API server-to-server.)
builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                  .WithHeaders("Content-Type", "Authorization");
        }
    });
});

// ---- Owner authentication (phone OTP + short-lived JWT + rotating refresh token) --------
// Keys come from configuration (Auth:JwtSigningKey, Auth:OtpHmacKey). See AuthSettings for the rules.
var authSettings = AuthSettings.Load(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddSingleton(authSettings);
builder.Services.AddSingleton<TokenService>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<ISmsSender, ConsoleSmsSender>();   // prints the code in the API console
}
else
{
    builder.Services.AddSingleton<ISmsSender, UnconfiguredSmsSender>();   // replaced by the MSG91 sender before launch
}
// Two bearer schemes, one per realm: both check the same signature, but each accepts only its own
// audience. Pinning the scheme to the policy is what stops an owner token working on an admin route.
var tokenService = new TokenService(authSettings);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokenService.ValidationParameters;
    })
    .AddJwtBearer(AdminAccess.Scheme, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokenService.AdminValidationParameters;
    });
builder.Services.AddAuthorization(options =>
{
    // Separate audiences keep owner, admin and super-admin tokens from being accepted by each other's endpoints.
    options.AddPolicy("Owner", policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
    options.AddPolicy(AdminAccess.Policy, policy => policy
        .AddAuthenticationSchemes(AdminAccess.Scheme)
        .RequireAuthenticatedUser());
});

// Baseline per-IP limit. Stricter, database-backed OTP limits are in OwnerAuthEndpoints.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalPermitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
});

var app = builder.Build();

// ---- Pipeline ------------------------------------------------------------------------
// Behind Azure App Service the real client IP arrives in X-Forwarded-For. Set the environment
// variable ASPNETCORE_FORWARDEDHEADERS_ENABLED=true there so rate limiting sees real IPs.
app.UseExceptionHandler();   // returns a generic problem response, never a stack trace
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        headers["Cache-Control"] = "no-store";   // API responses must not be cached by default
    }
    await next();
});

app.UseRateLimiter();
app.UseCors("ConfiguredOrigins");
app.UseAuthentication();
app.UseAuthorization();

// ---- Endpoints -----------------------------------------------------------------------
// live = the process is up (runs no checks, so a database outage never restarts the app).
// ready = the process can reach its database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// Route groups follow the audience split: /public, /me (owners), /admin, /super-admin.
var publicApi = app.MapGroup("/api/v1/public");
publicApi.MapGet("/meta", () => Results.Ok(new
{
    service = "BusinessDirectory.Api",
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
}));
publicApi.MapPublicDirectoryEndpoints();
publicApi.MapPublicSearchEndpoints();

app.MapOwnerAuthEndpoints();   // /api/v1/auth/owner: request code, verify code, refresh, logout
app.MapMeEndpoints();          // /api/v1/me: signed-in owner

app.MapAdminAuthEndpoints();   // /api/v1/auth/admin: email + password login, refresh, logout
app.MapAdminEndpoints();       // /api/v1/admin: signed-in administrator, listing moderation

app.Run();

// Makes the entry point visible to the integration test project.
public partial class Program;
