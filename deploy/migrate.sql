CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE "Workspaces" (
    "Id" uuid NOT NULL,
    "Name" character varying(120) NOT NULL,
    CONSTRAINT "PK_Workspaces" PRIMARY KEY ("Id")
);

CREATE TABLE "Memberships" (
    "WorkspaceId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Role" integer NOT NULL,
    "Active" boolean NOT NULL,
    CONSTRAINT "PK_Memberships" PRIMARY KEY ("WorkspaceId", "UserId"),
    CONSTRAINT "FK_Memberships_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Projects" (
    "Id" uuid NOT NULL,
    "WorkspaceId" uuid NOT NULL,
    "CreatorId" uuid NOT NULL,
    "Name" character varying(120) NOT NULL,
    "Archived" boolean NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Projects" PRIMARY KEY ("Id"),
    CONSTRAINT "AK_Projects_WorkspaceId_Id" UNIQUE ("WorkspaceId", "Id"),
    CONSTRAINT "FK_Projects_Memberships_WorkspaceId_CreatorId" FOREIGN KEY ("WorkspaceId", "CreatorId") REFERENCES "Memberships" ("WorkspaceId", "UserId") ON DELETE RESTRICT
);

CREATE TABLE "ProjectShares" (
    "WorkspaceId" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Access" integer NOT NULL,
    CONSTRAINT "PK_ProjectShares" PRIMARY KEY ("WorkspaceId", "ProjectId", "UserId"),
    CONSTRAINT "FK_ProjectShares_Memberships_WorkspaceId_UserId" FOREIGN KEY ("WorkspaceId", "UserId") REFERENCES "Memberships" ("WorkspaceId", "UserId") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProjectShares_Projects_WorkspaceId_ProjectId" FOREIGN KEY ("WorkspaceId", "ProjectId") REFERENCES "Projects" ("WorkspaceId", "Id") ON DELETE RESTRICT
);

CREATE TABLE "Tasks" (
    "Id" uuid NOT NULL,
    "WorkspaceId" uuid NOT NULL,
    "ProjectId" uuid,
    "InboxOwnerId" uuid,
    "CreatorId" uuid NOT NULL,
    "Title" character varying(500) NOT NULL,
    "Description" character varying(20000) NOT NULL,
    "DueKind" integer NOT NULL,
    "DueDate" date,
    "DueLocalTime" time without time zone,
    "DueZone" character varying(100),
    "DueAtUtc" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    "DeletedAt" timestamp with time zone,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Tasks" PRIMARY KEY ("Id"),
    CONSTRAINT "AK_Tasks_WorkspaceId_Id" UNIQUE ("WorkspaceId", "Id"),
    CONSTRAINT "CK_Task_Container" CHECK (("ProjectId" IS NULL) <> ("InboxOwnerId" IS NULL)),
    CONSTRAINT "CK_Task_Due" CHECK (("DueKind" = 0 AND "DueDate" IS NULL AND "DueLocalTime" IS NULL AND "DueZone" IS NULL AND "DueAtUtc" IS NULL) OR ("DueKind" = 1 AND "DueDate" IS NOT NULL AND "DueLocalTime" IS NULL AND "DueZone" IS NULL AND "DueAtUtc" IS NULL) OR ("DueKind" = 2 AND "DueDate" IS NOT NULL AND "DueLocalTime" IS NOT NULL AND "DueZone" IS NOT NULL AND "DueAtUtc" IS NOT NULL)),
    CONSTRAINT "FK_Tasks_Memberships_WorkspaceId_CreatorId" FOREIGN KEY ("WorkspaceId", "CreatorId") REFERENCES "Memberships" ("WorkspaceId", "UserId") ON DELETE RESTRICT,
    CONSTRAINT "FK_Tasks_Memberships_WorkspaceId_InboxOwnerId" FOREIGN KEY ("WorkspaceId", "InboxOwnerId") REFERENCES "Memberships" ("WorkspaceId", "UserId") ON DELETE RESTRICT,
    CONSTRAINT "FK_Tasks_Projects_WorkspaceId_ProjectId" FOREIGN KEY ("WorkspaceId", "ProjectId") REFERENCES "Projects" ("WorkspaceId", "Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_Projects_WorkspaceId_CreatorId" ON "Projects" ("WorkspaceId", "CreatorId");

CREATE INDEX "IX_ProjectShares_WorkspaceId_UserId" ON "ProjectShares" ("WorkspaceId", "UserId");

CREATE INDEX "IX_Tasks_WorkspaceId_CreatorId" ON "Tasks" ("WorkspaceId", "CreatorId");

CREATE INDEX "IX_Tasks_WorkspaceId_InboxOwnerId_Id" ON "Tasks" ("WorkspaceId", "InboxOwnerId", "Id");

CREATE INDEX "IX_Tasks_WorkspaceId_ProjectId_Id" ON "Tasks" ("WorkspaceId", "ProjectId", "Id");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001162643_InitialFoundation', '10.0.12');

COMMIT;

