#!/bin/sh
set -eu
psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 \
  --set=owner_password="$FINANCE_OWNER_PASSWORD" --set=app_password="$FINANCE_APP_PASSWORD" <<'SQL'
CREATE ROLE inklink_finance_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE inklink_finance_app LOGIN PASSWORD :'app_password';
CREATE DATABASE inklink_finance OWNER inklink_finance_owner;
REVOKE CONNECT ON DATABASE inklink_finance FROM PUBLIC;
GRANT CONNECT ON DATABASE inklink_finance TO inklink_finance_owner, inklink_finance_app;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
\connect inklink_finance
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA finance AUTHORIZATION inklink_finance_owner;
GRANT USAGE ON SCHEMA finance TO inklink_finance_app;
ALTER DEFAULT PRIVILEGES FOR ROLE inklink_finance_owner IN SCHEMA finance
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inklink_finance_app;
SQL
