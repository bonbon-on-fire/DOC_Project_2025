using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIChat.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "Email", "Name", "ProfileImageUrl", "Provider", "ProviderUserId", "UpdatedAt" },
                values: new object[] { "user-123", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "demo@aichat.com", "Demo User", null, "demo", null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: "user-123");
        }
    }
}
