using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Guildhall.AgentHost.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4ProjectsWorktreesAndRollbacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MemoryNotes_Scope_AgentDefinitionId_IsEnabled_UpdatedUtc",
                table: "MemoryNotes");

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "MemoryNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkspacePolicyOverride",
                table: "GuildhallBehaviorSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveWorktreeId",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RegisteredProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RootPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsGitRepository = table.Column<bool>(type: "bit", nullable: false),
                    DefaultBranch = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastOpenedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisteredProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolRollbackSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorktreeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Strategy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreStateRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PostStateRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Available"),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RolledBackUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolRollbackSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectWorktrees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    WorktreePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BaseCommitSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Active"),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectWorktrees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectWorktrees_RegisteredProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "RegisteredProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryNotes_ProjectId",
                table: "MemoryNotes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryNotes_Scope_AgentDefinitionId_ProjectId_IsEnabled_UpdatedUtc",
                table: "MemoryNotes",
                columns: new[] { "Scope", "AgentDefinitionId", "ProjectId", "IsEnabled", "UpdatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWorktrees_ConversationId_Status",
                table: "ProjectWorktrees",
                columns: new[] { "ConversationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWorktrees_ProjectId",
                table: "ProjectWorktrees",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredProjects_RootPath",
                table: "RegisteredProjects",
                column: "RootPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolRollbackSnapshots_RunId_RunEventId",
                table: "ToolRollbackSnapshots",
                columns: new[] { "RunId", "RunEventId" });

            migrationBuilder.AddForeignKey(
                name: "FK_MemoryNotes_RegisteredProjects_ProjectId",
                table: "MemoryNotes",
                column: "ProjectId",
                principalTable: "RegisteredProjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.RegisteredProjects TO [guildhall_runtime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.ProjectWorktrees TO [guildhall_runtime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.ToolRollbackSnapshots TO [guildhall_runtime];
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL
                BEGIN
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.RegisteredProjects FROM [guildhall_runtime];
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.ProjectWorktrees FROM [guildhall_runtime];
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.ToolRollbackSnapshots FROM [guildhall_runtime];
                END
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_MemoryNotes_RegisteredProjects_ProjectId",
                table: "MemoryNotes");

            migrationBuilder.DropTable(
                name: "ProjectWorktrees");

            migrationBuilder.DropTable(
                name: "ToolRollbackSnapshots");

            migrationBuilder.DropTable(
                name: "RegisteredProjects");

            migrationBuilder.DropIndex(
                name: "IX_MemoryNotes_ProjectId",
                table: "MemoryNotes");

            migrationBuilder.DropIndex(
                name: "IX_MemoryNotes_Scope_AgentDefinitionId_ProjectId_IsEnabled_UpdatedUtc",
                table: "MemoryNotes");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "MemoryNotes");

            migrationBuilder.DropColumn(
                name: "WorkspacePolicyOverride",
                table: "GuildhallBehaviorSettings");

            migrationBuilder.DropColumn(
                name: "ActiveWorktreeId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Conversations");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryNotes_Scope_AgentDefinitionId_IsEnabled_UpdatedUtc",
                table: "MemoryNotes",
                columns: new[] { "Scope", "AgentDefinitionId", "IsEnabled", "UpdatedUtc" });
        }
    }
}
