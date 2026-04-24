-- Migration AddAssignedPromptIdsToTenant — aplicación manual idempotente

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.name = 'Tenants' AND c.name = 'AssignedPromptIds'
)
BEGIN
    ALTER TABLE [Tenants] ADD [AssignedPromptIds] NVARCHAR(MAX) NOT NULL
        CONSTRAINT [DF_Tenants_AssignedPromptIds] DEFAULT '';
    PRINT 'ADD COLUMN Tenants.AssignedPromptIds';
END
ELSE
BEGIN
    PRINT 'Tenants.AssignedPromptIds ya existe';
END

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260424021228_AddAssignedPromptIdsToTenant')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260424021228_AddAssignedPromptIdsToTenant', N'10.0.0-preview.3.25171.6');
    PRINT 'EF history updated';
END

SELECT TOP 3 MigrationId FROM [__EFMigrationsHistory] ORDER BY MigrationId DESC;
