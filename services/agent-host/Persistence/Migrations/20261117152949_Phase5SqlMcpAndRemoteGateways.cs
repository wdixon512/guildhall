using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Guildhall.AgentHost.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5SqlMcpAndRemoteGateways : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowPrivateTunnelHttp",
                table: "McpServerRegistrations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SqlMcpProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Server = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Database = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AuthMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "integrated"),
                    AllowedSchemasJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false, defaultValue: "[\"dbo\"]"),
                    AllowedObjectsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false, defaultValue: "[]"),
                    MaxRows = table.Column<int>(type: "int", nullable: false, defaultValue: 100),
                    CommandTimeoutSeconds = table.Column<int>(type: "int", nullable: false, defaultValue: 10),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SqlMcpProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SqlMcpProfiles_Name",
                table: "SqlMcpProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.Sql(
                """
                IF DATABASE_PRINCIPAL_ID('guildhall_runtime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.SqlMcpProfiles TO [guildhall_runtime];
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SqlMcpProfiles");

            migrationBuilder.DropColumn(
                name: "AllowPrivateTunnelHttp",
                table: "McpServerRegistrations");
        }
    }
}
