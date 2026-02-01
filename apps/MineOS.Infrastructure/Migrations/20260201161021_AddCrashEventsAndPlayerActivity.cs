using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrashEventsAndPlayerActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrashEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DetectedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CrashType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CrashDetails = table.Column<string>(type: "TEXT", nullable: true),
                    AutoRestartAttempted = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoRestartSucceeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    RestartAttemptedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrashEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerActivityEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PlayerUuid = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    PlayerName = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EventData = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerActivityEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PlayerUuid = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    PlayerName = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    JoinedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LeftAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    LeaveReason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrashEvents_DetectedAt",
                table: "CrashEvents",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CrashEvents_ServerName",
                table: "CrashEvents",
                column: "ServerName");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityEvents_PlayerUuid",
                table: "PlayerActivityEvents",
                column: "PlayerUuid");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityEvents_ServerName",
                table: "PlayerActivityEvents",
                column: "ServerName");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityEvents_ServerName_PlayerUuid",
                table: "PlayerActivityEvents",
                columns: new[] { "ServerName", "PlayerUuid" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityEvents_ServerName_Timestamp",
                table: "PlayerActivityEvents",
                columns: new[] { "ServerName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityEvents_Timestamp",
                table: "PlayerActivityEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_JoinedAt",
                table: "PlayerSessions",
                column: "JoinedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_PlayerUuid",
                table: "PlayerSessions",
                column: "PlayerUuid");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_ServerName",
                table: "PlayerSessions",
                column: "ServerName");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_ServerName_PlayerUuid",
                table: "PlayerSessions",
                columns: new[] { "ServerName", "PlayerUuid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrashEvents");

            migrationBuilder.DropTable(
                name: "PlayerActivityEvents");

            migrationBuilder.DropTable(
                name: "PlayerSessions");
        }
    }
}
