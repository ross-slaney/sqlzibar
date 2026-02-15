-- Sqlzibar Schema v1: Initial table creation
-- All statements are idempotent (IF NOT EXISTS) for safe execution on existing databases.

-- 1. PrincipalTypes
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{PrincipalTypes}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{PrincipalTypes}] (
        [Id]          NVARCHAR(450)  NOT NULL,
        [Name]        NVARCHAR(MAX)  NOT NULL,
        [Description] NVARCHAR(MAX)  NULL,
        CONSTRAINT [PK_{PrincipalTypes}] PRIMARY KEY ([Id])
    );
END
GO

-- 2. Principals
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{Principals}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{Principals}] (
        [Id]              NVARCHAR(450)  NOT NULL,
        [PrincipalTypeId] NVARCHAR(450)  NOT NULL,
        [OrganizationId]  NVARCHAR(450)  NULL,
        [ExternalRef]     NVARCHAR(450)  NULL,
        [DisplayName]     NVARCHAR(MAX)  NOT NULL,
        [CreatedAt]       DATETIME2      NOT NULL,
        [UpdatedAt]       DATETIME2      NOT NULL,
        CONSTRAINT [PK_{Principals}] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_{Principals}_{PrincipalTypes}_PrincipalTypeId] FOREIGN KEY ([PrincipalTypeId])
            REFERENCES [{Schema}].[{PrincipalTypes}] ([Id]) ON DELETE NO ACTION
    );
END
GO

-- 3. ResourceTypes
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{ResourceTypes}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{ResourceTypes}] (
        [Id]          NVARCHAR(450)  NOT NULL,
        [Name]        NVARCHAR(MAX)  NOT NULL,
        [Description] NVARCHAR(MAX)  NULL,
        CONSTRAINT [PK_{ResourceTypes}] PRIMARY KEY ([Id])
    );
END
GO

-- 4. Resources
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{Resources}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{Resources}] (
        [Id]             NVARCHAR(450)  NOT NULL,
        [ParentId]       NVARCHAR(450)  NULL,
        [Name]           NVARCHAR(MAX)  NOT NULL,
        [Description]    NVARCHAR(MAX)  NULL,
        [ResourceTypeId] NVARCHAR(450)  NOT NULL,
        [IsActive]       BIT            NOT NULL DEFAULT 1,
        [CreatedAt]      DATETIME2      NOT NULL,
        [UpdatedAt]      DATETIME2      NOT NULL,
        CONSTRAINT [PK_{Resources}] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_{Resources}_{Resources}_ParentId] FOREIGN KEY ([ParentId])
            REFERENCES [{Schema}].[{Resources}] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_{Resources}_{ResourceTypes}_ResourceTypeId] FOREIGN KEY ([ResourceTypeId])
            REFERENCES [{Schema}].[{ResourceTypes}] ([Id]) ON DELETE NO ACTION
    );
END
GO

-- 5. Roles
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{Roles}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{Roles}] (
        [Id]          NVARCHAR(450)  NOT NULL,
        [Key]         NVARCHAR(MAX)  NOT NULL,
        [Name]        NVARCHAR(MAX)  NOT NULL,
        [Description] NVARCHAR(MAX)  NULL,
        [IsVirtual]   BIT            NOT NULL DEFAULT 0,
        CONSTRAINT [PK_{Roles}] PRIMARY KEY ([Id])
    );
END
GO

-- 6. Permissions
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{Permissions}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{Permissions}] (
        [Id]             NVARCHAR(450)  NOT NULL,
        [ResourceTypeId] NVARCHAR(450)  NULL,
        [Key]            NVARCHAR(MAX)  NOT NULL,
        [Name]           NVARCHAR(MAX)  NOT NULL,
        [Description]    NVARCHAR(MAX)  NULL,
        CONSTRAINT [PK_{Permissions}] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_{Permissions}_{ResourceTypes}_ResourceTypeId] FOREIGN KEY ([ResourceTypeId])
            REFERENCES [{Schema}].[{ResourceTypes}] ([Id]) ON DELETE NO ACTION
    );
END
GO

-- 7. RolePermissions
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{RolePermissions}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{RolePermissions}] (
        [RoleId]       NVARCHAR(450)  NOT NULL,
        [PermissionId] NVARCHAR(450)  NOT NULL,
        CONSTRAINT [PK_{RolePermissions}] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_{RolePermissions}_{Roles}_RoleId] FOREIGN KEY ([RoleId])
            REFERENCES [{Schema}].[{Roles}] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_{RolePermissions}_{Permissions}_PermissionId] FOREIGN KEY ([PermissionId])
            REFERENCES [{Schema}].[{Permissions}] ([Id]) ON DELETE NO ACTION
    );
END
GO

-- 8. Grants
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{Grants}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{Grants}] (
        [Id]            NVARCHAR(450)  NOT NULL,
        [PrincipalId]   NVARCHAR(450)  NOT NULL,
        [ResourceId]    NVARCHAR(450)  NOT NULL,
        [RoleId]        NVARCHAR(450)  NOT NULL,
        [EffectiveFrom] DATETIME2      NULL,
        [EffectiveTo]   DATETIME2      NULL,
        [CreatedAt]     DATETIME2      NOT NULL,
        [UpdatedAt]     DATETIME2      NOT NULL,
        CONSTRAINT [PK_{Grants}] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_{Grants}_{Principals}_PrincipalId] FOREIGN KEY ([PrincipalId])
            REFERENCES [{Schema}].[{Principals}] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_{Grants}_{Resources}_ResourceId] FOREIGN KEY ([ResourceId])
            REFERENCES [{Schema}].[{Resources}] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_{Grants}_{Roles}_RoleId] FOREIGN KEY ([RoleId])
            REFERENCES [{Schema}].[{Roles}] ([Id]) ON DELETE NO ACTION
    );
END
GO

-- 9. UserGroups
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{UserGroups}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{UserGroups}] (
        [Id]          NVARCHAR(450)  NOT NULL,
        [Name]        NVARCHAR(MAX)  NOT NULL,
        [Description] NVARCHAR(MAX)  NULL,
        [GroupType]    NVARCHAR(MAX)  NULL,
        [PrincipalId] NVARCHAR(450)  NOT NULL,
        [CreatedAt]   DATETIME2      NOT NULL,
        [UpdatedAt]   DATETIME2      NOT NULL,
        CONSTRAINT [PK_{UserGroups}] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_{UserGroups}_{Principals}_PrincipalId] FOREIGN KEY ([PrincipalId])
            REFERENCES [{Schema}].[{Principals}] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [UQ_{UserGroups}_PrincipalId] UNIQUE ([PrincipalId])
    );
END
GO

-- 10. UserGroupMemberships
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{UserGroupMemberships}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{UserGroupMemberships}] (
        [PrincipalId]  NVARCHAR(450)  NOT NULL,
        [UserGroupId]  NVARCHAR(450)  NOT NULL,
        [CreatedAt]    DATETIME2      NOT NULL,
        CONSTRAINT [PK_{UserGroupMemberships}] PRIMARY KEY ([PrincipalId], [UserGroupId]),
        CONSTRAINT [FK_{UserGroupMemberships}_{Principals}_PrincipalId] FOREIGN KEY ([PrincipalId])
            REFERENCES [{Schema}].[{Principals}] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_{UserGroupMemberships}_{UserGroups}_UserGroupId] FOREIGN KEY ([UserGroupId])
            REFERENCES [{Schema}].[{UserGroups}] ([Id]) ON DELETE NO ACTION
    );
END
GO

-- 11. ServiceAccounts
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{ServiceAccounts}' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[{ServiceAccounts}] (
        [Id]               NVARCHAR(450)  NOT NULL,
        [PrincipalId]      NVARCHAR(450)  NOT NULL,
        [ClientId]         NVARCHAR(MAX)  NOT NULL,
        [ClientSecretHash] NVARCHAR(MAX)  NOT NULL,
        [Description]      NVARCHAR(MAX)  NULL,
        [LastUsedAt]       DATETIME2      NULL,
        [ExpiresAt]        DATETIME2      NULL,
        [CreatedAt]        DATETIME2      NOT NULL,
        [UpdatedAt]        DATETIME2      NOT NULL,
        CONSTRAINT [PK_{ServiceAccounts}] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_{ServiceAccounts}_{Principals}_PrincipalId] FOREIGN KEY ([PrincipalId])
            REFERENCES [{Schema}].[{Principals}] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [UQ_{ServiceAccounts}_PrincipalId] UNIQUE ([PrincipalId])
    );
END
GO

-- 12. SqlzibarSchema (version tracking)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SqlzibarSchema' AND schema_id = SCHEMA_ID('{Schema}'))
BEGIN
    CREATE TABLE [{Schema}].[SqlzibarSchema] (
        [Version] INT NOT NULL
    );
END
GO

-- Set schema version to 1
IF NOT EXISTS (SELECT 1 FROM [{Schema}].[SqlzibarSchema])
BEGIN
    INSERT INTO [{Schema}].[SqlzibarSchema] ([Version]) VALUES (1);
END
ELSE
BEGIN
    UPDATE [{Schema}].[SqlzibarSchema] SET [Version] = 1 WHERE [Version] < 1;
END
