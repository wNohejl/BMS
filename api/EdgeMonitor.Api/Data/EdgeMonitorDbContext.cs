using EdgeMonitor.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EdgeMonitor.Api.Data;

public class EdgeMonitorDbContext : DbContext
{
    public EdgeMonitorDbContext(DbContextOptions<EdgeMonitorDbContext> options) : base(options)
    {
    }

    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ZoneControl> ZoneControls => Set<ZoneControl>();
    public DbSet<ZoneStatus> ZoneStatuses => Set<ZoneStatus>();
    public DbSet<DeviceCommand> DeviceCommands => Set<DeviceCommand>();
    public DbSet<TwinAlarm> TwinAlarms => Set<TwinAlarm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SensorReading>()
            .HasIndex(r => new { r.TenantId, r.ZoneId, r.TimestampUtc });

        modelBuilder.Entity<SensorReading>()
            .HasIndex(r => new { r.TenantId, r.DeviceId });

        modelBuilder.Entity<Alert>()
            .HasIndex(a => new { a.TenantId, a.ZoneId, a.SensorType });

        modelBuilder.Entity<ZoneControl>()
            .HasIndex(c => new { c.TenantId, c.ZoneId })
            .IsUnique();

        modelBuilder.Entity<ZoneStatus>()
            .HasIndex(s => new { s.TenantId, s.ZoneId })
            .IsUnique();

        modelBuilder.Entity<DeviceCommand>()
            .HasIndex(c => new { c.TenantId, c.ZoneId, c.SentUtc });

        modelBuilder.Entity<TwinAlarm>()
            .HasIndex(a => new { a.TenantId, a.IsActive });
    }
}
