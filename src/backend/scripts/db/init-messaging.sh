#!/bin/sh
set -eu
psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 \
  --set=owner_password="$MESSAGING_OWNER_PASSWORD" --set=app_password="$MESSAGING_APP_PASSWORD" <<'SQL'
CREATE ROLE inklink_messaging_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE inklink_messaging_app LOGIN PASSWORD :'app_password';
CREATE DATABASE inklink_messaging OWNER inklink_messaging_owner;
REVOKE CONNECT ON DATABASE inklink_messaging FROM PUBLIC;
GRANT CONNECT ON DATABASE inklink_messaging TO inklink_messaging_owner, inklink_messaging_app;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
\connect inklink_messaging
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA messaging AUTHORIZATION inklink_messaging_owner;
GRANT USAGE ON SCHEMA messaging TO inklink_messaging_app;
ALTER DEFAULT PRIVILEGES FOR ROLE inklink_messaging_owner IN SCHEMA messaging
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inklink_messaging_app;
SQL
