BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260330011630_AddWebhookLog'
)
BEGIN
    CREATE TABLE [WebhookLogs] (
        [Id] uniqueidentifier NOT NULL,
        [Provider] nvarchar(max) NOT NULL,
        [InstanceId] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NULL,
        [FromPhone] nvarchar(max) NULL,
        [MessageType] nvarchar(max) NULL,
        [Body] nvarchar(max) NULL,
        [ExternalMessageId] nvarchar(max) NULL,
        [Status] nvarchar(max) NOT NULL,
        [StatusReason] nvarchar(max) NULL,
        [RawPayload] nvarchar(max) NULL,
        [ReceivedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WebhookLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260330011630_AddWebhookLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260330011630_AddWebhookLog', N'10.0.0-preview.3.25171.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [Tenants] ADD [CampaignMessageDelaySeconds] int NOT NULL DEFAULT 10;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [Campaigns] ADD [LaunchedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [Campaigns] ADD [LaunchedByUserId] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [Campaigns] ADD [Status] nvarchar(30) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [CampaignContacts] ADD [ClaimedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [CampaignContacts] ADD [ContactDataJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [CampaignContacts] ADD [DispatchAttempts] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [CampaignContacts] ADD [DispatchError] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [CampaignContacts] ADD [DispatchStatus] nvarchar(30) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [CampaignContacts] ADD [ExternalMessageId] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [CampaignContacts] ADD [GeneratedMessage] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    ALTER TABLE [CampaignContacts] ADD [SentAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AppUsers]') AND [c].[name] = N'AvatarUrl');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [AppUsers] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [AppUsers] ALTER COLUMN [AvatarUrl] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    CREATE TABLE [CampaignDispatchLogs] (
        [Id] uniqueidentifier NOT NULL,
        [CampaignId] uniqueidentifier NOT NULL,
        [CampaignContactId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [AttemptNumber] int NOT NULL,
        [PromptSnapshot] nvarchar(max) NULL,
        [ContactDataSnapshot] nvarchar(max) NULL,
        [GeneratedMessage] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(20) NOT NULL,
        [UltraMsgResponse] nvarchar(max) NULL,
        [ExternalMessageId] nvarchar(200) NULL,
        [Status] nvarchar(20) NOT NULL,
        [ErrorDetail] nvarchar(2000) NULL,
        [DurationMs] int NOT NULL,
        [OccurredAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CampaignDispatchLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    CREATE INDEX [IX_CampaignContacts_CampaignId_DispatchStatus_ClaimedAt] ON [CampaignContacts] ([CampaignId], [DispatchStatus], [ClaimedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    CREATE INDEX [IX_CampaignDispatchLogs_CampaignContactId] ON [CampaignDispatchLogs] ([CampaignContactId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    CREATE INDEX [IX_CampaignDispatchLogs_CampaignId_OccurredAt] ON [CampaignDispatchLogs] ([CampaignId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403152422_AddCampaignMessageDelayToTenant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260403152422_AddCampaignMessageDelayToTenant', N'10.0.0-preview.3.25171.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403195245_AddNotifyPhoneAndTransferChat'
)
BEGIN
    ALTER TABLE [Campaigns] ADD [LaunchedByUserPhone] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403195245_AddNotifyPhoneAndTransferChat'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [NotifyPhone] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260403195245_AddNotifyPhoneAndTransferChat'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260403195245_AddNotifyPhoneAndTransferChat', N'10.0.0-preview.3.25171.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260404170021_AddAttentionScheduleToCampaignTemplate'
)
BEGIN
    ALTER TABLE [CampaignTemplates] ADD [AttentionDays] nvarchar(100) NOT NULL DEFAULT N'[1,2,3,4,5]';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260404170021_AddAttentionScheduleToCampaignTemplate'
)
BEGIN
    ALTER TABLE [CampaignTemplates] ADD [AttentionEndTime] nvarchar(5) NOT NULL DEFAULT N'17:00';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260404170021_AddAttentionScheduleToCampaignTemplate'
)
BEGIN
    ALTER TABLE [CampaignTemplates] ADD [AttentionStartTime] nvarchar(5) NOT NULL DEFAULT N'08:00';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260404170021_AddAttentionScheduleToCampaignTemplate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260404170021_AddAttentionScheduleToCampaignTemplate', N'10.0.0-preview.3.25171.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409025008_AddUserPermissions'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CampaignTemplates]') AND [c].[name] = N'AttentionStartTime');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [CampaignTemplates] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [CampaignTemplates] ADD DEFAULT ('08:00') FOR [AttentionStartTime];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409025008_AddUserPermissions'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CampaignTemplates]') AND [c].[name] = N'AttentionEndTime');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [CampaignTemplates] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [CampaignTemplates] ADD DEFAULT ('17:00') FOR [AttentionEndTime];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409025008_AddUserPermissions'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CampaignTemplates]') AND [c].[name] = N'AttentionDays');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [CampaignTemplates] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [CampaignTemplates] ADD DEFAULT ('[1,2,3,4,5]') FOR [AttentionDays];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409025008_AddUserPermissions'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [Permissions] nvarchar(2000) NOT NULL DEFAULT N'[]';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409025008_AddUserPermissions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260409025008_AddUserPermissions', N'10.0.0-preview.3.25171.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409125424_AddTenantIdToActionDefinition'
)
BEGIN
    DROP INDEX [IX_ActionDefinitions_Name] ON [ActionDefinitions];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409125424_AddTenantIdToActionDefinition'
)
BEGIN
    ALTER TABLE [ActionDefinitions] ADD [TenantId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409125424_AddTenantIdToActionDefinition'
)
BEGIN

                    -- Paso 1: asignar al tenant del CampaignTemplate que referencia la acción
                    UPDATE ad
                    SET ad.TenantId = ct.TenantId
                    FROM ActionDefinitions ad
                    CROSS APPLY (
                        SELECT TOP 1 ct2.TenantId
                        FROM CampaignTemplates ct2
                        WHERE ct2.ActionIds LIKE '%' + CAST(ad.Id AS NVARCHAR(36)) + '%'
                    ) ct
                    WHERE ad.TenantId = '00000000-0000-0000-0000-000000000000';

                    -- Paso 2: las que no matchearon, asignar al primer tenant activo
                    UPDATE ActionDefinitions
                    SET TenantId = (SELECT TOP 1 Id FROM Tenants WHERE IsActive = 1 ORDER BY CreatedAt)
                    WHERE TenantId = '00000000-0000-0000-0000-000000000000';
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409125424_AddTenantIdToActionDefinition'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ActionDefinitions_TenantId_Name] ON [ActionDefinitions] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409125424_AddTenantIdToActionDefinition'
)
BEGIN
    ALTER TABLE [ActionDefinitions] ADD CONSTRAINT [FK_ActionDefinitions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409125424_AddTenantIdToActionDefinition'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260409125424_AddTenantIdToActionDefinition', N'10.0.0-preview.3.25171.6');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409135502_ExpandActionConfigsToMax'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260409135502_ExpandActionConfigsToMax', N'10.0.0-preview.3.25171.6');
END;

COMMIT;
GO

