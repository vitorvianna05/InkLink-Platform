#!/bin/sh
set -eu
psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 \
  --set=owner_password="$IDENTITY_OWNER_PASSWORD" --set=app_password="$IDENTITY_APP_PASSWORD" <<'SQL'
CREATE ROLE inklink_identity_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE inklink_identity_app LOGIN PASSWORD :'app_password';
CREATE DATABASE inklink_identity OWNER inklink_identity_owner;
REVOKE CONNECT ON DATABASE inklink_identity FROM PUBLIC;
GRANT CONNECT ON DATABASE inklink_identity TO inklink_identity_owner, inklink_identity_app;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
\connect inklink_identity
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA identity AUTHORIZATION inklink_identity_owner;
GRANT USAGE ON SCHEMA identity TO inklink_identity_app;
ALTER DEFAULT PRIVILEGES FOR ROLE inklink_identity_owner IN SCHEMA identity
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inklink_identity_app;
SQL
