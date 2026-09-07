-- PostgreSQL translation of the matching SQL Server script.
-- Durable AuthPage cookie families. Silent renewal must inherit the same
-- family so logout can invalidate superseded predecessors. Unlinked cookies
-- issued before this revision are consumed so they cannot keep the
-- predecessor-replay hole after upgrade.

DO $sqlos_guard$
BEGIN
  IF to_regclass(format('%I.%I', '{Schema}', 'SqlOSUsers')) IS NOT NULL THEN
    CREATE TABLE IF NOT EXISTS "{Schema}"."SqlOSAuthPageSessionFamilies"
    (
        "Id" varchar(64) NOT NULL,
        "UserId" varchar(64) NOT NULL,
        "OrganizationId" varchar(64) NULL,
        "CreatedAt" timestamp NOT NULL,
        "RevokedAt" timestamp NULL,
        "RevocationReason" varchar(200) NULL,
        CONSTRAINT "PK_SqlOSAuthPageSessionFamilies" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SqlOSAuthPageSessionFamilies_User" FOREIGN KEY ("UserId") REFERENCES "{Schema}"."SqlOSUsers"("Id"),
        CONSTRAINT "FK_SqlOSAuthPageSessionFamilies_Organization" FOREIGN KEY ("OrganizationId") REFERENCES "{Schema}"."SqlOSOrganizations"("Id")
    );
  END IF;
END
$sqlos_guard$;

DO $sqlos_guard$
BEGIN
  IF to_regclass(format('%I.%I', '{Schema}', 'SqlOSAuthPageSessionFamilies')) IS NOT NULL
     AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = '{Schema}' AND table_name = 'SqlOSAuthPageSessionFamilies' AND column_name = 'UserId') THEN
    CREATE INDEX IF NOT EXISTS "IX_SqlOSAuthPageSessionFamilies_UserId_RevokedAt"
        ON "{Schema}"."SqlOSAuthPageSessionFamilies"("UserId", "RevokedAt");
  END IF;
END
$sqlos_guard$;

DO $sqlos_guard$
BEGIN
  IF to_regclass(format('%I.%I', '{Schema}', 'SqlOSAuthPageSessionFamilies')) IS NOT NULL
     AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = '{Schema}' AND table_name = 'SqlOSAuthPageSessionFamilies' AND column_name = 'OrganizationId') THEN
    CREATE INDEX IF NOT EXISTS "IX_SqlOSAuthPageSessionFamilies_OrganizationId_RevokedAt"
        ON "{Schema}"."SqlOSAuthPageSessionFamilies"("OrganizationId", "RevokedAt");
  END IF;
END
$sqlos_guard$;

ALTER TABLE IF EXISTS "{Schema}"."SqlOSTemporaryTokens"
    ADD COLUMN IF NOT EXISTS "AuthPageSessionFamilyId" varchar(64) NULL;

DO $sqlos_guard$
BEGIN
  IF to_regclass(format('%I.%I', '{Schema}', 'SqlOSTemporaryTokens')) IS NOT NULL
     AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = '{Schema}' AND table_name = 'SqlOSTemporaryTokens' AND column_name = 'AuthPageSessionFamilyId') THEN
    CREATE INDEX IF NOT EXISTS "IX_SqlOSTemporaryTokens_AuthPageSessionFamilyId"
        ON "{Schema}"."SqlOSTemporaryTokens"("AuthPageSessionFamilyId");
  END IF;
END
$sqlos_guard$;

DO $sqlos_guard$
BEGIN
  IF to_regclass(format('%I.%I', '{Schema}', 'SqlOSTemporaryTokens')) IS NOT NULL
     AND to_regclass(format('%I.%I', '{Schema}', 'SqlOSAuthPageSessionFamilies')) IS NOT NULL
     AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = '{Schema}' AND table_name = 'SqlOSTemporaryTokens' AND column_name = 'AuthPageSessionFamilyId')
     AND NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_SqlOSTemporaryTokens_AuthPageSessionFamily') THEN
    ALTER TABLE IF EXISTS "{Schema}"."SqlOSTemporaryTokens"
        ADD CONSTRAINT "FK_SqlOSTemporaryTokens_AuthPageSessionFamily"
            FOREIGN KEY ("AuthPageSessionFamilyId") REFERENCES "{Schema}"."SqlOSAuthPageSessionFamilies"("Id");
  END IF;
END
$sqlos_guard$;

DO $sqlos_guard$
BEGIN
  IF to_regclass(format('%I.%I', '{Schema}', 'SqlOSTemporaryTokens')) IS NOT NULL
     AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = '{Schema}' AND table_name = 'SqlOSTemporaryTokens' AND column_name = 'AuthPageSessionFamilyId') THEN
    UPDATE "{Schema}"."SqlOSTemporaryTokens"
    SET "ConsumedAt" = CURRENT_TIMESTAMP
    WHERE "Purpose" = 'auth_page_session'
      AND "ConsumedAt" IS NULL
      AND "AuthPageSessionFamilyId" IS NULL;
  END IF;
END
$sqlos_guard$;
