using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConvertPerformanceMetricTimestampToUnix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE PerformanceMetrics_temp (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ServerName TEXT NOT NULL,
    Timestamp INTEGER NOT NULL,
    CpuPercent REAL NOT NULL,
    RamUsedMb INTEGER NOT NULL,
    RamTotalMb INTEGER NOT NULL,
    Tps REAL NULL,
    PlayerCount INTEGER NOT NULL
);
INSERT INTO PerformanceMetrics_temp (Id, ServerName, Timestamp, CpuPercent, RamUsedMb, RamTotalMb, Tps, PlayerCount)
SELECT Id,
       ServerName,
       CASE
           WHEN typeof(Timestamp) = 'integer' THEN Timestamp
           WHEN typeof(Timestamp) = 'real' THEN CAST(Timestamp AS INTEGER)
           WHEN Timestamp IS NULL OR Timestamp = '' THEN 0
           ELSE COALESCE(CAST(strftime('%s', Timestamp) AS INTEGER), 0)
       END,
       CpuPercent,
       RamUsedMb,
       RamTotalMb,
       Tps,
       PlayerCount
FROM PerformanceMetrics;
DROP TABLE PerformanceMetrics;
ALTER TABLE PerformanceMetrics_temp RENAME TO PerformanceMetrics;
CREATE INDEX IX_PerformanceMetrics_ServerName_Timestamp ON PerformanceMetrics (ServerName, Timestamp);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE PerformanceMetrics_temp (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ServerName TEXT NOT NULL,
    Timestamp TEXT NOT NULL,
    CpuPercent REAL NOT NULL,
    RamUsedMb INTEGER NOT NULL,
    RamTotalMb INTEGER NOT NULL,
    Tps REAL NULL,
    PlayerCount INTEGER NOT NULL
);
INSERT INTO PerformanceMetrics_temp (Id, ServerName, Timestamp, CpuPercent, RamUsedMb, RamTotalMb, Tps, PlayerCount)
SELECT Id,
       ServerName,
       CASE
           WHEN typeof(Timestamp) = 'text' THEN Timestamp
           WHEN Timestamp IS NULL THEN '1970-01-01T00:00:00Z'
           ELSE strftime('%Y-%m-%dT%H:%M:%SZ', Timestamp, 'unixepoch')
       END,
       CpuPercent,
       RamUsedMb,
       RamTotalMb,
       Tps,
       PlayerCount
FROM PerformanceMetrics;
DROP TABLE PerformanceMetrics;
ALTER TABLE PerformanceMetrics_temp RENAME TO PerformanceMetrics;
CREATE INDEX IX_PerformanceMetrics_ServerName_Timestamp ON PerformanceMetrics (ServerName, Timestamp);
");
        }
    }
}
