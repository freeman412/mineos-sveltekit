using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModpackSourceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstalledModpacks_ServerName_CurseForgeProjectId",
                table: "InstalledModpacks");

            migrationBuilder.AlterColumn<int>(
                name: "CurseForgeProjectId",
                table: "InstalledModpacks",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "InstalledModpacks",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "curseforge");

            migrationBuilder.AddColumn<string>(
                name: "SourceProjectId",
                table: "InstalledModpacks",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE InstalledModpacks SET SourceProjectId = CAST(CurseForgeProjectId AS TEXT) WHERE SourceProjectId IS NULL AND CurseForgeProjectId IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledModpacks_ServerName_Source_SourceProjectId",
                table: "InstalledModpacks",
                columns: new[] { "ServerName", "Source", "SourceProjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstalledModpacks_ServerName_Source_SourceProjectId",
                table: "InstalledModpacks");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "InstalledModpacks");

            migrationBuilder.DropColumn(
                name: "SourceProjectId",
                table: "InstalledModpacks");

            migrationBuilder.AlterColumn<int>(
                name: "CurseForgeProjectId",
                table: "InstalledModpacks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstalledModpacks_ServerName_CurseForgeProjectId",
                table: "InstalledModpacks",
                columns: new[] { "ServerName", "CurseForgeProjectId" },
                unique: true);
        }
    }
}
