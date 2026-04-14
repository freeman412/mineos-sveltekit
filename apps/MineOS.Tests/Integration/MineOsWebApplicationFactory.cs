using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Tests.Integration;

public class MineOsWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"TestDb_{Guid.NewGuid():N}";
    private const string TestSigningKey = "test-signing-key-at-least-32-characters-long!!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:SeedUsername"] = "admin",
                ["Auth:SeedPassword"] = "admin123!",
                ["Auth:Jwt:Issuer"] = "MineOS.Test",
                ["Auth:Jwt:Audience"] = "MineOS.Test",
                ["Auth:Jwt:SigningKey"] = TestSigningKey,
                ["Auth:Jwt:ExpiresMinutes"] = "60",
                ["ApiKey:StaticKey"] = "dev-static-api-key-change-me",
                ["ConnectionStrings:Default"] = "DataSource=:memory:",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registrations
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType == typeof(IDbContextFactory<AppDbContext>))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
                services.Remove(descriptor);

            // Add in-memory database
            services.AddDbContextFactory<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            // Remove background services that don't work well in test environment
            var hostedServiceDescriptors = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();

            foreach (var descriptor in hostedServiceDescriptors)
                services.Remove(descriptor);

            // Remove the original JwtBearerOptions configure action that captured
            // an empty signing key (Program.cs reads config eagerly at build time).
            // Then replace with our test config.
            var jwtConfigDescriptors = services
                .Where(d => d.ServiceType == typeof(IConfigureOptions<JwtBearerOptions>))
                .ToList();

            foreach (var descriptor in jwtConfigDescriptors)
                services.Remove(descriptor);

            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = "MineOS.Test",
                    ValidAudience = "MineOS.Test",
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(TestSigningKey))
                };
            });
        });
    }
}
