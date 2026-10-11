using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIChatTools.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddChatMessageFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Feedback",
                table: "ChatMessages",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Feedback",
                table: "ChatMessages");
        }
    }
}
