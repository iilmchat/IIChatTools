using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIChatTools.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddChatMessageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "ChatMessages",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "ChatMessages");
        }
    }
}
