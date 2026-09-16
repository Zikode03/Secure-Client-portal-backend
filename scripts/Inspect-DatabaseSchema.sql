SET NOCOUNT ON;
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;
SELECT SCHEMA_NAME(schema_id) AS SchemaName, name FROM sys.tables WHERE name LIKE '%MigrationsHistory%';
SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory_Banking', N'U') IS NOT NULL
    SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory_Banking ORDER BY MigrationId;
SELECT t.name AS TableName, c.name AS ColumnName, ty.name AS SqlType, c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.name IN ('AppComplianceMonitoringProfiles', 'AppComplianceCheckSettings',
    'AppComplianceVerifications', 'AppComplianceAutomationConfigurations', 'AppComplianceObligations') OR t.name LIKE 'AppBank%'
ORDER BY t.name, c.column_id;
SELECT t.name AS TableName, i.name AS IndexName, i.is_unique,
    STRING_AGG(CAST(c.name AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS [Columns]
FROM sys.tables t JOIN sys.indexes i ON i.object_id = t.object_id
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE (t.name LIKE 'AppCompliance%' OR t.name LIKE 'AppBank%') AND ic.key_ordinal > 0
GROUP BY t.name, i.name, i.is_unique ORDER BY t.name, i.name;
SELECT fk.name AS ForeignKeyName, OBJECT_NAME(fk.parent_object_id) AS FromTable,
    OBJECT_NAME(fk.referenced_object_id) AS ToTable, fk.delete_referential_action_desc, fk.is_disabled, fk.is_not_trusted
FROM sys.foreign_keys fk WHERE OBJECT_NAME(fk.parent_object_id) LIKE 'AppBank%' OR OBJECT_NAME(fk.parent_object_id) LIKE 'AppCompliance%'
ORDER BY FromTable, ForeignKeyName;
SELECT OBJECT_NAME(parent_object_id) AS TableName, name, definition, is_disabled, is_not_trusted
FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) LIKE 'AppBank%';
SELECT t.name AS TableName, SUM(p.rows) AS [RowCount]
FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE t.name LIKE 'AppBank%' OR t.name LIKE 'AppCompliance%' GROUP BY t.name ORDER BY t.name;
