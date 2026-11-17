using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Guildhall.AgentHost.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemoryNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemoryNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AgentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SourceConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemoryNotes_AgentDefinitions_AgentDefinitionId",
                        column: x => x.AgentDefinitionId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryNotes_AgentDefinitionId",
                table: "MemoryNotes",
                column: "AgentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryNotes_Scope_AgentDefinitionId_IsEnabled_UpdatedUtc",
                table: "MemoryNotes",
                columns: new[] { "Scope", "AgentDefinitionId", "IsEnabled", "UpdatedUtc" });

            migrationBuilder.Sql("ALTER TABLE [dbo].[MemoryNotes] ADD CONSTRAINT [CK_MemoryNotes_ScopeOwnership] CHECK (([Scope] = N'AboutYou' AND [AgentDefinitionId] IS NULL) OR ([Scope] = N'Member' AND [AgentDefinitionId] IS NOT NULL));");
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.MemoryNotes TO [guildhall_runtime];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.MemoryNotes FROM [guildhall_runtime];");
            migrationBuilder.DropTable(
                name: "MemoryNotes");
        }
    }
}
