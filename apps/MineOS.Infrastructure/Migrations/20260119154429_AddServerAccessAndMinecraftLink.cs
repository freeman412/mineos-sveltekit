using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServerAccessAndMinecraftLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MinecraftUsername",
                table: "Users",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MinecraftUuid",
                table: "Users",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServerAccesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CanView = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanControl = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanConsole = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerAccesses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerAccesses_UserId_ServerName",
                table: "ServerAccesses",
                columns: new[] { "UserId", "ServerName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerAccesses");

            migrationBuilder.DropColumn(
                name: "MinecraftUsername",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MinecraftUuid",
                table: "Users");
        }
    }
}
