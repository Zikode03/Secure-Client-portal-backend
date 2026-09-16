IF DB_NAME() <> N'secure_client_portal_dev'
    THROW 51006, 'This backup is authorised for the DEVELOPMENT database only.', 1;
DECLARE @directory nvarchar(4000) = CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000));
IF @directory IS NULL THROW 51007, 'SQL Server did not provide a default backup directory.', 1;
IF RIGHT(@directory, 1) NOT IN (N'/', N'\')
    SET @directory = @directory + CASE WHEN CHARINDEX(N'\', @directory) > 0 THEN N'\' ELSE N'/' END;
DECLARE @backupFile nvarchar(4000) = @directory + N'secure_client_portal_dev_migration_verification_'
    + CONVERT(char(8), GETUTCDATE(), 112) + N'_' + REPLACE(CONVERT(char(8), GETUTCDATE(), 108), ':', '') + N'.bak';
BACKUP DATABASE [secure_client_portal_dev] TO DISK = @backupFile WITH COPY_ONLY, CHECKSUM, STATS = 10;
RESTORE VERIFYONLY FROM DISK = @backupFile WITH CHECKSUM;
SELECT @backupFile AS BackupFile;
