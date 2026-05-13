-- Migration MoveDocumentsFromAgentToCampaignTemplate — aplicación manual idempotente

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AgentDocuments')
BEGIN
    DROP TABLE [AgentDocuments];
    PRINT 'DROP TABLE AgentDocuments';
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CampaignTemplateDocuments')
BEGIN
    CREATE TABLE [CampaignTemplateDocuments] (
        [Id] uniqueidentifier NOT NULL,
        [CampaignTemplateId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(500) NOT NULL,
        [BlobUrl] nvarchar(2000) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [FileSizeBytes] bigint NOT NULL,
        [UploadedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CampaignTemplateDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CampaignTemplateDocuments_CampaignTemplates_CampaignTemplateId]
            FOREIGN KEY ([CampaignTemplateId]) REFERENCES [CampaignTemplates] ([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_CampaignTemplateDocuments_CampaignTemplateId]
        ON [CampaignTemplateDocuments] ([CampaignTemplateId]);

    CREATE INDEX [IX_CampaignTemplateDocuments_TenantId]
        ON [CampaignTemplateDocuments] ([TenantId]);

    PRINT 'CREATE TABLE CampaignTemplateDocuments';
END

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260423022143_MoveDocumentsFromAgentToCampaignTemplate')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260423022143_MoveDocumentsFromAgentToCampaignTemplate', N'10.0.0-preview.3.25171.6');
    PRINT 'EF migration history updated';
END

SELECT TOP 1 MigrationId FROM [__EFMigrationsHistory] ORDER BY MigrationId DESC;
