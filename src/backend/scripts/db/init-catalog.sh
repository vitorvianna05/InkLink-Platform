#!/bin/sh
set -eu
psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 \
  --set=owner_password="$CATALOG_OWNER_PASSWORD" --set=app_password="$CATALOG_APP_PASSWORD" <<'SQL'
CREATE ROLE inklink_catalog_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE inklink_catalog_app LOGIN PASSWORD :'app_password';
CREATE DATABASE inklink_catalog OWNER inklink_catalog_owner;
REVOKE CONNECT ON DATABASE inklink_catalog FROM PUBLIC;
GRANT CONNECT ON DATABASE inklink_catalog TO inklink_catalog_owner, inklink_catalog_app;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
\connect inklink_catalog
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA catalog AUTHORIZATION inklink_catalog_owner;
GRANT USAGE ON SCHEMA catalog TO inklink_catalog_app;
ALTER DEFAULT PRIVILEGES FOR ROLE inklink_catalog_owner IN SCHEMA catalog
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inklink_catalog_app;
SQL
