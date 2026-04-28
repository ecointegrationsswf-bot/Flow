IF COL_LENGTH('Tenants', 'LabelingAnalysisPrompt') IS NULL
  ALTER TABLE Tenants ADD LabelingAnalysisPrompt nvarchar(max) NULL;

IF COL_LENGTH('Tenants', 'LabelingResultSchemaPrompt') IS NULL
  ALTER TABLE Tenants ADD LabelingResultSchemaPrompt nvarchar(max) NULL;

IF COL_LENGTH('Conversations', 'LabelingResultJson') IS NULL
  ALTER TABLE Conversations ADD LabelingResultJson nvarchar(max) NULL;

PRINT 'Columns ready';
