START TRANSACTION;
DROP TABLE "ProjectShares";

DROP TABLE "Tasks";

DROP TABLE "Projects";

DROP TABLE "Memberships";

DROP TABLE "Workspaces";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261001162643_InitialFoundation';

COMMIT;

