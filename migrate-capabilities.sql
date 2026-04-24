-- Migration ExpandAgentRegistryCapabilitiesTo4000 — aplicación manual idempotente

IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.name = 'AgentRegistryEntries'
      AND c.name = 'Capabilities'
      AND c.max_length < 8000  -- nvarchar(500) = 1000 bytes, nvarchar(4000) = 8000 bytes
)
BEGIN
    ALTER TABLE [AgentRegistryEntries] ALTER COLUMN [Capabilities] NVARCHAR(4000) NOT NULL;
    PRINT 'ALTER Capabilities -> nvarchar(4000)';
END
ELSE
BEGIN
    PRINT 'Capabilities ya esta en nvarchar(4000) o mayor';
END

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260424013253_ExpandAgentRegistryCapabilitiesTo4000')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260424013253_ExpandAgentRegistryCapabilitiesTo4000', N'10.0.0-preview.3.25171.6');
    PRINT 'EF history updated';
END

SELECT TOP 3 MigrationId FROM [__EFMigrationsHistory] ORDER BY MigrationId DESC;
