BEGIN TRANSACTION;
ALTER TABLE [AgentDefinitions] ADD [IsArchived] bit NOT NULL DEFAULT CAST(0 AS bit);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261116021040_CouncilMemberArchive', N'10.0.15');

COMMIT;
GO

