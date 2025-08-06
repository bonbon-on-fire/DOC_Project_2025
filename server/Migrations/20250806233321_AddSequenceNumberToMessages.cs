using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIChat.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSequenceNumberToMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SequenceNumber",
                table: "Messages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Populate sequence numbers for existing messages based on timestamp order
            migrationBuilder.Sql(@"
                WITH RankedMessages AS (
                    SELECT Id, ChatId, 
                           ROW_NUMBER() OVER (PARTITION BY ChatId ORDER BY Timestamp) - 1 AS SequenceNum
                    FROM Messages
                )
                UPDATE Messages 
                SET SequenceNumber = RankedMessages.SequenceNum
                FROM RankedMessages
                WHERE Messages.Id = RankedMessages.Id;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatId_SequenceNumber",
                table: "Messages",
                columns: new[] { "ChatId", "SequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ChatId_SequenceNumber",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SequenceNumber",
                table: "Messages");
        }
    }
}
