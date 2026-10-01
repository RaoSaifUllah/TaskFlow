-- REVIEW TEMPLATE ONLY. Do not execute until the database is confirmed TaskFlow-only.
-- Use the owner's direct (non-pooler) migration connection. Generate passwords
-- locally and save only in ignored configuration. Never commit populated SQL.
-- Replace the placeholder passwords in a local ignored copy, not this example.
CREATE ROLE taskflow_read LOGIN PASSWORD 'GENERATE_LOCALLY' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
CREATE ROLE taskflow_write LOGIN PASSWORD 'GENERATE_LOCALLY' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
-- Apply EF migrations with the owner, then deploy/restricted-roles.sql.
-- Keep the owner out of API/worker Write/Read settings.
-- TestMigration/TestRead should target a separate disposable DB/Neon branch.
-- Do not roll back or drop an owner's shared neondb database.
