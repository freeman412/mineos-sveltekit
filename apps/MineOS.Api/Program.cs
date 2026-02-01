using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using System.Threading;
using MineOS.Api.Middleware;
using MineOS.Api.Endpoints;
using MineOS.Application.Interfaces;
using HostOptions = MineOS.Application.Options.HostOptions;
using ApiKeyOptions = MineOS.Application.Options.ApiKeyOptions;
using JwtOptions = MineOS.Application.Options.JwtOptions;
using CurseForgeOptions = MineOS.Application.Options.CurseForgeOptions;
using MineOS.Infrastructure.Persistence;
using MineOS.Infrastructure.Persistence.Repositories;
using MineOS.Infrastructure.Services;
using MineOS.Infrastructure.External;
using MineOS.Infrastructure.Background;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();

builder.Host.UseSerilog((context, services, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.AddEndpointsApiExplorer();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
});

// Configure JSON serialization to use camelCase for JavaScript/TypeScript interop
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MineOS.Api",
        Version = "v1"
    });

    // API Key authentication (for server-to-backend calls)
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API key needed to access protected endpoints. Use header: X-Api-Key",
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey
    });

    // JWT Bearer authentication (for user login)
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    // Add security requirements using OpenApiSecuritySchemeReference (required for Microsoft.OpenApi 2.x)
    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("ApiKey", doc), new List<string>() },
        { new OpenApiSecuritySchemeReference("Bearer", doc), new List<string>() }
    });
});

builder.Services.Configure<HostOptions>(builder.Configuration.GetSection("Host"));
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("ApiKey"));
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration("Auth:Jwt")
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey),
        "JWT signing key must be configured. Set Auth:Jwt:SigningKey in appsettings.json or AUTH__JWT__SIGNINGKEY environment variable.")
    .ValidateOnStart();
builder.Services.Configure<CurseForgeOptions>(builder.Configuration.GetSection("CurseForge"));

var jwtOptions = builder.Configuration.GetSection("Auth:Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrWhiteSpace(context.Token)
                    && context.Request.Cookies.TryGetValue("auth_token", out var token)
                    && !string.IsNullOrWhiteSpace(token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseSqlite(connectionString);
});
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    options.UseSqlite(connectionString);
}, ServiceLifetime.Singleton);
builder.Services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddSingleton<IModpackRepository, ModpackRepository>();
builder.Services.AddScoped<IApiKeyValidator, ApiKeyValidator>();
builder.Services.AddScoped<IHostService, HostService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IServerAccessService, ServerAccessService>();
builder.Services.AddScoped<IServerService, ServerService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<IArchiveService, ArchiveService>();
builder.Services.AddScoped<IClientPackageService, ClientPackageService>();
builder.Services.AddScoped<IConsoleService, ConsoleService>();
builder.Services.AddScoped<IMonitoringService, MonitoringService>();
builder.Services.AddScoped<IPerformanceService, PerformanceService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<IPluginService, PluginService>();
builder.Services.AddScoped<ICurseForgeService, CurseForgeService>();
builder.Services.AddScoped<IWorldService, WorldService>();
builder.Services.AddScoped<IPlayerService, PlayerService>();
builder.Services.AddScoped<IPlayerActivityService, PlayerActivityService>();
builder.Services.AddHttpClient<IForgeService, ForgeService>();
builder.Services.AddHttpClient<IMojangApiService, MojangApiService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAdminShellSession, AdminShellService>();
builder.Services.AddSingleton<IProcessManager, ProcessManager>();
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<BackgroundJobService>();
builder.Services.AddSingleton<IBackgroundJobService>(sp => sp.GetRequiredService<BackgroundJobService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackgroundJobService>());
builder.Services.AddHostedService<PerformanceCollectorService>();
builder.Services.AddHostedService<LanBroadcastService>();
builder.Services.AddHostedService<StartupServerService>();
builder.Services.AddHostedService<ApplicationLifetimeService>();
builder.Services.AddHostedService<TelemetryReporterService>();
builder.Services.AddSingleton<WatchdogService>();
builder.Services.AddSingleton<IWatchdogService>(sp => sp.GetRequiredService<WatchdogService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<WatchdogService>());
builder.Services.AddHttpClient<IProfileService, ProfileService>();
builder.Services.AddHttpClient<IModService, ModService>(client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<IModrinthService, ModrinthService>(client =>
{
    client.BaseAddress = new Uri("https://api.modrinth.com/v2/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MineOS/1.0");
});
builder.Services.AddHttpClient<CurseForgeClient>();
builder.Services.AddScoped<ApiKeySeeder>();
builder.Services.AddScoped<UserSeeder>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddHttpClient<ITelemetryService, TelemetryService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod();

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin();
        }
    });
});

var app = builder.Build();


app.UseCors("DevCors");
app.UseWebSockets();
app.UseSwagger();
app.UseSwaggerUI();

app.UseSerilogRequestLogging();
app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ApiKeyMiddleware>();

var connectionString = builder.Configuration.GetConnectionString("Default");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);
    if (!string.IsNullOrWhiteSpace(sqliteBuilder.DataSource))
    {
        var dbPath = Path.GetFullPath(sqliteBuilder.DataSource);
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Run migrations on startup
    await db.Database.MigrateAsync();

    // Seed initial data
    var seeder = scope.ServiceProvider.GetRequiredService<ApiKeySeeder>();
    await seeder.EnsureSeedAsync(CancellationToken.None);
    var userSeeder = scope.ServiceProvider.GetRequiredService<UserSeeder>();
    await userSeeder.EnsureSeedAsync(CancellationToken.None);
}

app.MapApiEndpoints();

app.Run();
