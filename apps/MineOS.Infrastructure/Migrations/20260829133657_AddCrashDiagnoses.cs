using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrashDiagnoses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrashDiagnoses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CrashEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RedactedInput = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    LikelyCause = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedActions = table.Column<string>(type: "TEXT", nullable: true),
                    Classification = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Confidence = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrashDiagnoses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrashDiagnoses_ServerName_CrashEventId",
                table: "CrashDiagnoses",
                columns: new[] { "ServerName", "CrashEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrashDiagnoses_ServerName_SourceHash",
                table: "CrashDiagnoses",
                columns: new[] { "ServerName", "SourceHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrashDiagnoses");
        }
    }
}
