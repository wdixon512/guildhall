using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Guildhall.AgentHost.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CouncilProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Accent",
                table: "AgentDefinitions",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#71465f");

            migrationBuilder.AddColumn<string>(
                name: "CustomPortrait",
                table: "AgentDefinitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PortraitId",
                table: "AgentDefinitions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "AgentDefinitions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Steward");

            migrationBuilder.Sql(@"
DECLARE @conversationId uniqueidentifier, @sourceId uniqueidentifier, @profileId uniqueidentifier;
DECLARE legacy_profiles CURSOR LOCAL FAST_FORWARD FOR
    SELECT [Id], [AgentDefinitionId] FROM [dbo].[Conversations] ORDER BY [CreatedUtc], [Id];
OPEN legacy_profiles;
FETCH NEXT FROM legacy_profiles INTO @conversationId, @sourceId;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @profileId = NEWID();
    INSERT INTO [dbo].[AgentDefinitions] ([Id], [Name], [Instructions], [DefaultModelId], [Revision], [Role], [PortraitId], [CustomPortrait], [Accent])
    SELECT @profileId, [Name], [Instructions], [DefaultModelId], 1, [Role], [PortraitId], [CustomPortrait], [Accent]
    FROM [dbo].[AgentDefinitions] WHERE [Id] = @sourceId;
    UPDATE [dbo].[Conversations] SET [AgentDefinitionId] = @profileId WHERE [Id] = @conversationId;
    FETCH NEXT FROM legacy_profiles INTO @conversationId, @sourceId;
END;
CLOSE legacy_profiles;
DEALLOCATE legacy_profiles;");
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL GRANT INSERT ON OBJECT::dbo.AgentDefinitions TO [guildhall_runtime];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL REVOKE INSERT ON OBJECT::dbo.AgentDefinitions FROM [guildhall_runtime];");
            migrationBuilder.DropColumn(
                name: "Accent",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "CustomPortrait",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "PortraitId",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "AgentDefinitions");
        }
    }
}
