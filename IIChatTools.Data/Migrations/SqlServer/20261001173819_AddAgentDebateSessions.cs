using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIChatTools.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddAgentDebateSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentDebateSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChatId = table.Column<int>(type: "int", nullable: false),
                    InitiatedByUserId = table.Column<int>(type: "int", nullable: false),
                    Task = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PatternType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FinalVerdict = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FinalArtifactJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfigSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalCostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalTokensIn = table.Column<int>(type: "int", nullable: false),
                    TotalTokensOut = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDebateSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentDebateSessions_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentDebateRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    RoundNumber = table.Column<int>(type: "int", nullable: false),
                    ActorOutput = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CriticVerdict = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CriticFeedbackJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActorModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CriticModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WasEscalated = table.Column<bool>(type: "bit", nullable: false),
                    EscalationProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TokensIn = table.Column<int>(type: "int", nullable: false),
                    TokensOut = table.Column<int>(type: "int", nullable: false),
                    CostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDebateRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentDebateRounds_AgentDebateSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AgentDebateSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentDebateRounds_Session_RoundNumber",
                table: "AgentDebateRounds",
                columns: new[] { "SessionId", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentDebateSessions_ChatId_StartedAt",
                table: "AgentDebateSessions",
                columns: new[] { "ChatId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentDebateSessions_User_Status",
                table: "AgentDebateSessions",
                columns: new[] { "InitiatedByUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentDebateRounds");

            migrationBuilder.DropTable(
                name: "AgentDebateSessions");
        }
    }
}
