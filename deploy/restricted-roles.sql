-- Run as migration owner after migration. Provision LOGIN roles/passwords outside this file.
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO taskflow_read, taskflow_write;
GRANT SELECT ON "Workspaces", "Memberships", "Projects", "ProjectShares", "Tasks" TO taskflow_read;
GRANT SELECT, INSERT, UPDATE, DELETE ON "Workspaces", "Memberships", "Projects", "ProjectShares", "Tasks" TO taskflow_write;
-- Do not grant identity secrets or future tables automatically to the read role.
