using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Guildhall.AgentHost.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConversationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedUtc",
                table: "Conversations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Conversations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL GRANT DELETE ON OBJECT::dbo.Conversations TO [guildhall_runtime];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL REVOKE DELETE ON OBJECT::dbo.Conversations FROM [guildhall_runtime];");
            migrationBuilder.DropColumn(
                name: "ArchivedUtc",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Conversations");
        }
    }
}
