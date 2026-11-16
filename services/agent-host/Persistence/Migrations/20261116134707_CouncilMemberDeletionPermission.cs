using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Guildhall.AgentHost.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CouncilMemberDeletionPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL GRANT DELETE ON OBJECT::dbo.AgentDefinitions TO [guildhall_runtime];");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL REVOKE DELETE ON OBJECT::dbo.AgentDefinitions FROM [guildhall_runtime];");

        }
    }
}
