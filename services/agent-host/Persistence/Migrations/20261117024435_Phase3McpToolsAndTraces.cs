using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Guildhall.AgentHost.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3McpToolsAndTraces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PermissionPolicyOverride",
                table: "Conversations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkspaceRootPath",
                table: "Conversations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnabledToolIdsJson",
                table: "AgentDefinitions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "[\"filesystem-mcp\",\"git-mcp\"]");

            migrationBuilder.AddColumn<string>(
                name: "PermissionPolicy",
                table: "AgentDefinitions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "ask_unsafe");

            migrationBuilder.CreateTable(
                name: "ChatRunEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SecurityTier = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Tier1"),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Running"),
                    ArgumentsRedactedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultRedactedJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRunEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatRunEvents_ChatRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ChatRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpServerRegistrations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Transport = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Command = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ArgumentsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false, defaultValue: "[]"),
                    EndpointUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SecurityTier = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Tier3"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpServerRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRunEvents_RunId_SequenceNumber",
                table: "ChatRunEvents",
                columns: new[] { "RunId", "SequenceNumber" },
                unique: true);

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.ChatRunEvents TO [guildhall_runtime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.McpServerRegistrations TO [guildhall_runtime];
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL
                BEGIN
                    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.ChatRunEvents FROM [guildhall_runtime];
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.McpServerRegistrations FROM [guildhall_runtime];
                END
                """);

            migrationBuilder.DropTable(
                name: "ChatRunEvents");

            migrationBuilder.DropTable(
                name: "McpServerRegistrations");

            migrationBuilder.DropColumn(
                name: "PermissionPolicyOverride",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "WorkspaceRootPath",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "EnabledToolIdsJson",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "PermissionPolicy",
                table: "AgentDefinitions");
        }
    }
}
