using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MineOS.Infrastructure.Persistence;

#nullable disable

namespace MineOS.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260124091500_AddNotificationRecipients")]
    public partial class AddNotificationRecipients : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecipientUserId",
                table: "SystemNotifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemNotifications_RecipientUserId",
                table: "SystemNotifications",
                column: "RecipientUserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemNotifications_RecipientUserId",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "RecipientUserId",
                table: "SystemNotifications");
        }
    }
}
