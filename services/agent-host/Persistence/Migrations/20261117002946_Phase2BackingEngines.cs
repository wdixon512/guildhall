using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Guildhall.AgentHost.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2BackingEngines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SelectedHarnessMode",
                table: "Conversations",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedProviderId",
                table: "Conversations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostSource",
                table: "ChatRuns",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "ChatRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EngineKind",
                table: "ChatRuns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "local");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "ChatRuns",
                type: "decimal(10,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "ChatRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "ChatRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultHarnessMode",
                table: "AgentDefinitions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultProviderId",
                table: "AgentDefinitions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "ollama");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelectedHarnessMode",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "SelectedProviderId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "CostSource",
                table: "ChatRuns");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "ChatRuns");

            migrationBuilder.DropColumn(
                name: "EngineKind",
                table: "ChatRuns");

            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
                table: "ChatRuns");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "ChatRuns");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "ChatRuns");

            migrationBuilder.DropColumn(
                name: "DefaultHarnessMode",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "DefaultProviderId",
                table: "AgentDefinitions");
        }
    }
}
