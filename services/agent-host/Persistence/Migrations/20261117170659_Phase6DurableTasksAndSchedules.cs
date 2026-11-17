using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Guildhall.AgentHost.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6DurableTasksAndSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledAutomations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AgentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Prompt = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    CronExpression = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TimezoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "UTC"),
                    MisfirePolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "RunOnce"),
                    MaxCatchUpRuns = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    MaxBudgetTokens = table.Column<int>(type: "int", nullable: true),
                    MaxDurationSeconds = table.Column<int>(type: "int", nullable: false, defaultValue: 300),
                    IsPaused = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastEvaluatedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextRunUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledAutomations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledAutomations_AgentDefinitions_AgentDefinitionId",
                        column: x => x.AgentDefinitionId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledAutomations_RegisteredProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "RegisteredProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DurableTaskRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduledAutomationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TriggerKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Scheduled"),
                    ScheduledForUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Queued"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SummaryText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DurableTaskRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DurableTaskRuns_ChatRuns_ChatRunId",
                        column: x => x.ChatRunId,
                        principalTable: "ChatRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DurableTaskRuns_ScheduledAutomations_ScheduledAutomationId",
                        column: x => x.ScheduledAutomationId,
                        principalTable: "ScheduledAutomations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DurableTaskRuns_ChatRunId",
                table: "DurableTaskRuns",
                column: "ChatRunId");

            migrationBuilder.CreateIndex(
                name: "IX_DurableTaskRuns_ScheduledAutomationId_CreatedUtc",
                table: "DurableTaskRuns",
                columns: new[] { "ScheduledAutomationId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DurableTaskRuns_Status_ScheduledForUtc",
                table: "DurableTaskRuns",
                columns: new[] { "Status", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledAutomations_AgentDefinitionId",
                table: "ScheduledAutomations",
                column: "AgentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledAutomations_IsPaused_NextRunUtc",
                table: "ScheduledAutomations",
                columns: new[] { "IsPaused", "NextRunUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledAutomations_ProjectId",
                table: "ScheduledAutomations",
                column: "ProjectId");

            migrationBuilder.Sql(
                """
                IF DATABASE_PRINCIPAL_ID('guildhall_runtime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.ScheduledAutomations TO [guildhall_runtime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.DurableTaskRuns TO [guildhall_runtime];
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DurableTaskRuns");

            migrationBuilder.DropTable(
                name: "ScheduledAutomations");
        }
    }
}
