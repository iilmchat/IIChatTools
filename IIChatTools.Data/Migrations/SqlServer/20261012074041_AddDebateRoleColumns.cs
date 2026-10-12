using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIChatTools.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddDebateRoleColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContraOutput",
                table: "AgentDebateRounds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JudgeFeedbackJson",
                table: "AgentDebateRounds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JudgeVerdict",
                table: "AgentDebateRounds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProOutput",
                table: "AgentDebateRounds",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContraOutput",
                table: "AgentDebateRounds");

            migrationBuilder.DropColumn(
                name: "JudgeFeedbackJson",
                table: "AgentDebateRounds");

            migrationBuilder.DropColumn(
                name: "JudgeVerdict",
                table: "AgentDebateRounds");

            migrationBuilder.DropColumn(
                name: "ProOutput",
                table: "AgentDebateRounds");
        }
    }
}
