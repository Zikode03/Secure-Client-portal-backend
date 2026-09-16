IF DB_NAME() <> N'secure_client_portal_dev'
    THROW 51006, 'This history transition is authorised for the DEVELOPMENT database only.', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock @Resource = N'SecureClientPortal.BankingHistoryTransition',
    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
IF @lockResult < 0 THROW 51000, 'Could not acquire the Banking history transition lock.', 1;

DECLARE @alreadyAdopted bit = 0;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory_Banking', N'U') IS NOT NULL
    IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory_Banking WHERE MigrationId = N'20260916180000_BankingFoundation')
        SET @alreadyAdopted = 1;
IF @alreadyAdopted = 0
BEGIN
    DECLARE @legacyVersion nvarchar(32);
    IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
        SELECT @legacyVersion = ProductVersion FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260916180000_BankingFoundation';
    DECLARE @bankTableCount int = (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN
        (N'AppBankConnections', N'AppBankAccounts', N'AppBankTransactions', N'AppBankSyncRuns', N'AppBankConsentRecords'));
    IF @legacyVersion IS NOT NULL
    BEGIN
        IF @bankTableCount <> 5 THROW 51001, 'Legacy Banking history exists but the five Banking tables are not intact. No history was adopted.', 1;
        IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 0 AND is_not_trusted = 0 AND name IN
            (N'FK_AppBankConnections_AppClients_ClientId',
             N'FK_AppBankAccounts_AppBankConnections_BankConnectionId', N'FK_AppBankAccounts_AppClients_ClientId',
             N'FK_AppBankTransactions_AppBankAccounts_BankAccountId', N'FK_AppBankTransactions_AppClients_ClientId',
             N'FK_AppBankSyncRuns_AppBankConnections_BankConnectionId', N'FK_AppBankSyncRuns_AppClients_ClientId',
             N'FK_AppBankConsentRecords_AppBankConnections_BankConnectionId', N'FK_AppBankConsentRecords_AppClients_ClientId')) <> 9
            THROW 51002, 'Legacy Banking foreign keys are not intact. No history was adopted.', 1;
        IF (SELECT COUNT(*) FROM sys.indexes WHERE is_disabled = 0 AND name IN
            (N'IX_AppBankConnections_ClientId_Status', N'IX_AppBankConnections_Provider_ExternalConnectionId',
             N'IX_AppBankAccounts_BankConnectionId_ExternalAccountId', N'IX_AppBankAccounts_ClientId',
             N'IX_AppBankTransactions_BankAccountId_ExternalTransactionId', N'IX_AppBankTransactions_ClientId_TransactionDateUtc',
             N'IX_AppBankSyncRuns_BankConnectionId_StartedAtUtc', N'IX_AppBankSyncRuns_ClientId_StartedAtUtc',
             N'IX_AppBankConsentRecords_BankConnectionId', N'IX_AppBankConsentRecords_ClientId_GrantedAtUtc')) <> 10
            THROW 51003, 'Legacy Banking indexes are not intact. No history was adopted.', 1;
        IF (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled = 0 AND is_not_trusted = 0 AND name IN
            (N'CK_AppBankConnections_Status', N'CK_AppBankTransactions_Direction', N'CK_AppBankSyncRuns_Status')) <> 3
            THROW 51004, 'Legacy Banking check constraints are not intact. No history was adopted.', 1;
        IF OBJECT_ID(N'dbo.__EFMigrationsHistory_Banking', N'U') IS NULL
            CREATE TABLE dbo.__EFMigrationsHistory_Banking (
                MigrationId nvarchar(150) NOT NULL, ProductVersion nvarchar(32) NOT NULL,
                CONSTRAINT PK___EFMigrationsHistory_Banking PRIMARY KEY (MigrationId));
        INSERT INTO dbo.__EFMigrationsHistory_Banking (MigrationId, ProductVersion)
        VALUES (N'20260916180000_BankingFoundation', @legacyVersion);
    END
    ELSE IF @bankTableCount > 0
        THROW 51005, 'Banking tables exist without a recognised migration history. Refusing to recreate them.', 1;
END;
COMMIT TRANSACTION;
