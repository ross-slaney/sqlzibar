-- Durable AuthPage cookie families. Silent renewal must inherit the same
-- family so logout can invalidate superseded predecessors. Unlinked cookies
-- issued before this revision are consumed so they cannot keep the
-- predecessor-replay hole after upgrade.

IF OBJECT_ID(N'[{Schema}].[SqlOSUsers]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[{Schema}].[SqlOSAuthPageSessionFamilies]', N'U') IS NULL
BEGIN
    CREATE TABLE [{Schema}].[SqlOSAuthPageSessionFamilies]
    (
        [Id] NVARCHAR(64) NOT NULL,
        [UserId] NVARCHAR(64) NOT NULL,
        [OrganizationId] NVARCHAR(64) NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        [RevokedAt] DATETIME2 NULL,
        [RevocationReason] NVARCHAR(200) NULL,
        CONSTRAINT [PK_SqlOSAuthPageSessionFamilies] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SqlOSAuthPageSessionFamilies_User] FOREIGN KEY ([UserId]) REFERENCES [{Schema}].[SqlOSUsers]([Id]),
        CONSTRAINT [FK_SqlOSAuthPageSessionFamilies_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [{Schema}].[SqlOSOrganizations]([Id])
    );

    CREATE INDEX [IX_SqlOSAuthPageSessionFamilies_UserId_RevokedAt]
        ON [{Schema}].[SqlOSAuthPageSessionFamilies]([UserId], [RevokedAt]);
    CREATE INDEX [IX_SqlOSAuthPageSessionFamilies_OrganizationId_RevokedAt]
        ON [{Schema}].[SqlOSAuthPageSessionFamilies]([OrganizationId], [RevokedAt]);
END

GO

IF OBJECT_ID(N'[{Schema}].[SqlOSTemporaryTokens]', N'U') IS NOT NULL
   AND COL_LENGTH('[{Schema}].[SqlOSTemporaryTokens]', 'AuthPageSessionFamilyId') IS NULL
BEGIN
    ALTER TABLE [{Schema}].[SqlOSTemporaryTokens]
    ADD [AuthPageSessionFamilyId] NVARCHAR(64) NULL;
END

GO

IF OBJECT_ID(N'[{Schema}].[SqlOSTemporaryTokens]', N'U') IS NOT NULL
   AND COL_LENGTH('[{Schema}].[SqlOSTemporaryTokens]', 'AuthPageSessionFamilyId') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_SqlOSTemporaryTokens_AuthPageSessionFamilyId'
          AND object_id = OBJECT_ID(N'[{Schema}].[SqlOSTemporaryTokens]'))
BEGIN
    CREATE INDEX [IX_SqlOSTemporaryTokens_AuthPageSessionFamilyId]
        ON [{Schema}].[SqlOSTemporaryTokens]([AuthPageSessionFamilyId]);
END

GO

IF OBJECT_ID(N'[{Schema}].[SqlOSTemporaryTokens]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[{Schema}].[SqlOSAuthPageSessionFamilies]', N'U') IS NOT NULL
   AND COL_LENGTH('[{Schema}].[SqlOSTemporaryTokens]', 'AuthPageSessionFamilyId') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_SqlOSTemporaryTokens_AuthPageSessionFamily'
          AND parent_object_id = OBJECT_ID(N'[{Schema}].[SqlOSTemporaryTokens]'))
BEGIN
    ALTER TABLE [{Schema}].[SqlOSTemporaryTokens]
        ADD CONSTRAINT [FK_SqlOSTemporaryTokens_AuthPageSessionFamily]
            FOREIGN KEY ([AuthPageSessionFamilyId]) REFERENCES [{Schema}].[SqlOSAuthPageSessionFamilies]([Id]);
END

GO

IF OBJECT_ID(N'[{Schema}].[SqlOSTemporaryTokens]', N'U') IS NOT NULL
   AND COL_LENGTH('[{Schema}].[SqlOSTemporaryTokens]', 'AuthPageSessionFamilyId') IS NOT NULL
BEGIN
    UPDATE [{Schema}].[SqlOSTemporaryTokens]
    SET [ConsumedAt] = SYSUTCDATETIME()
    WHERE [Purpose] = N'auth_page_session'
      AND [ConsumedAt] IS NULL
      AND [AuthPageSessionFamilyId] IS NULL;
END
