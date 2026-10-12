using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIChatTools.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddOrchestratorSubtaskColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubtaskAgent",
                table: "AgentDebateRounds",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtaskError",
                table: "AgentDebateRounds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtaskId",
                table: "AgentDebateRounds",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtaskResult",
                table: "AgentDebateRounds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtaskStatus",
                table: "AgentDebateRounds",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtaskTask",
                table: "AgentDebateRounds",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubtaskAgent",
                table: "AgentDebateRounds");

            migrationBuilder.DropColumn(
                name: "SubtaskError",
                table: "AgentDebateRounds");

            migrationBuilder.DropColumn(
                name: "SubtaskId",
                table: "AgentDebateRounds");

            migrationBuilder.DropColumn(
                name: "SubtaskResult",
                table: "AgentDebateRounds");

            migrationBuilder.DropColumn(
                name: "SubtaskStatus",
                table: "AgentDebateRounds");

            migrationBuilder.DropColumn(
                name: "SubtaskTask",
                table: "AgentDebateRounds");
        }
    }
}
