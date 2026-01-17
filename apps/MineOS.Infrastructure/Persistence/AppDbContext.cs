using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Core entities
    public DbSet<User> Users => Set<User>();
    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    // Server management
    public DbSet<ServerNote> ServerNotes => Set<ServerNote>();
    public DbSet<ServerTag> ServerTags => Set<ServerTag>();
    public DbSet<UserFavorite> UserFavorites => Set<UserFavorite>();
    public DbSet<ServerTemplate> ServerTemplates => Set<ServerTemplate>();

    // Performance & Monitoring
    public DbSet<PerformanceMetric> PerformanceMetrics => Set<PerformanceMetric>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Player Management
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerBan> PlayerBans => Set<PlayerBan>();

    // Content & Config
    public DbSet<Plugin> Plugins => Set<Plugin>();
    public DbSet<World> Worlds => Set<World>();
    public DbSet<ResourcePack> ResourcePacks => Set<ResourcePack>();

    // Integration & Automation
    public DbSet<WebhookConfig> WebhookConfigs => Set<WebhookConfig>();
    public DbSet<MigrationHistory> MigrationHistories => Set<MigrationHistory>();

    // Multi-Host
    public DbSet<Host> Hosts => Set<Host>();

    // API & Security
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    // Mod Management
    public DbSet<InstalledModpack> InstalledModpacks => Set<InstalledModpack>();
    public DbSet<InstalledModRecord> InstalledModRecords => Set<InstalledModRecord>();

    // Notifications
    public DbSet<SystemNotification> SystemNotifications => Set<SystemNotification>();

    // Settings
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.Property(x => x.Key).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.UserId);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(128);
            entity.Property(x => x.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<JobRecord>(entity =>
        {
            entity.ToTable("Jobs");
            entity.HasKey(x => x.JobId);
            entity.Property(x => x.JobId).HasMaxLength(64);
            entity.Property(x => x.Type).HasMaxLength(64);
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.Status).HasMaxLength(32);
        });

        // Server management
        modelBuilder.Entity<ServerNote>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ServerName);
            entity.Property(x => x.ServerName).HasMaxLength(256);
        });

        modelBuilder.Entity<ServerTag>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ServerName);
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.Name).HasMaxLength(64);
            entity.Property(x => x.Color).HasMaxLength(32);
        });

        modelBuilder.Entity<UserFavorite>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.ServerName }).IsUnique();
            entity.Property(x => x.ServerName).HasMaxLength(256);
        });

        modelBuilder.Entity<ServerTemplate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(128);
        });

        // Performance & Monitoring
        modelBuilder.Entity<PerformanceMetric>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerName, x.Timestamp });
            entity.Property(x => x.ServerName).HasMaxLength(256);
            var timestampConverter = new ValueConverter<DateTimeOffset, long>(
                value => value.ToUnixTimeSeconds(),
                value => DateTimeOffset.FromUnixTimeSeconds(value));
            entity.Property(x => x.Timestamp)
                .HasConversion(timestampConverter)
                .HasColumnType("INTEGER");
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ServerName);
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.Type).HasMaxLength(64);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.Timestamp);
            entity.Property(x => x.Action).HasMaxLength(128);
            entity.Property(x => x.ServerName).HasMaxLength(256);
        });

        // Player Management
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerName, x.Uuid }).IsUnique();
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.Uuid).HasMaxLength(36);
            entity.Property(x => x.Name).HasMaxLength(16);
        });

        modelBuilder.Entity<PlayerBan>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PlayerId);
            entity.Property(x => x.BannedBy).HasMaxLength(128);
            entity.HasOne(x => x.Player)
                .WithMany()
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Content & Config
        modelBuilder.Entity<Plugin>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerName, x.Name }).IsUnique();
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Version).HasMaxLength(32);
        });

        modelBuilder.Entity<World>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerName, x.Name }).IsUnique();
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Type).HasMaxLength(64);
        });

        modelBuilder.Entity<ResourcePack>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ServerName);
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.Name).HasMaxLength(256);
        });

        // Integration & Automation
        modelBuilder.Entity<WebhookConfig>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ServerName);
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.Type).HasMaxLength(32);
        });

        modelBuilder.Entity<MigrationHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ServerName);
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.FromVersion).HasMaxLength(32);
            entity.Property(x => x.ToVersion).HasMaxLength(32);
            entity.Property(x => x.Status).HasMaxLength(32);
        });

        // Multi-Host
        modelBuilder.Entity<Host>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Hostname).HasMaxLength(256);
            entity.Property(x => x.Status).HasMaxLength(32);
        });

        // Mod Management
        modelBuilder.Entity<InstalledModpack>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerName, x.CurseForgeProjectId }).IsUnique();
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.Name).HasMaxLength(256);
            entity.Property(x => x.Version).HasMaxLength(64);
            entity.Property(x => x.LogoUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<InstalledModRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerName, x.FileName }).IsUnique();
            entity.Property(x => x.ServerName).HasMaxLength(256);
            entity.Property(x => x.FileName).HasMaxLength(256);
            entity.Property(x => x.ModName).HasMaxLength(256);
            entity.HasOne(x => x.Modpack)
                .WithMany(m => m.Mods)
                .HasForeignKey(x => x.ModpackId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Notifications
        modelBuilder.Entity<SystemNotification>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasMaxLength(32);
            entity.Property(x => x.Title).HasMaxLength(256);
            entity.Property(x => x.ServerName).HasMaxLength(256);

            // Convert DateTimeOffset to Unix timestamp for SQLite compatibility
            var timestampConverter = new ValueConverter<DateTimeOffset, long>(
                value => value.ToUnixTimeSeconds(),
                value => DateTimeOffset.FromUnixTimeSeconds(value));
            entity.Property(x => x.CreatedAt)
                .HasConversion(timestampConverter)
                .HasColumnType("INTEGER");
            entity.Property(x => x.DismissedAt)
                .HasConversion(timestampConverter)
                .HasColumnType("INTEGER");

            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => new { x.ServerName, x.CreatedAt });
        });

        // Settings
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(1024);
            entity.Property(x => x.Description).HasMaxLength(512);
        });
    }
}
