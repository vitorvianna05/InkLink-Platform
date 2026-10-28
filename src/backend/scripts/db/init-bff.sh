#!/bin/sh
set -eu
psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 \
 --set=owner_password="$BFF_OWNER_PASSWORD" --set=app_password="$BFF_APP_PASSWORD" <<'SQL'
CREATE ROLE inklink_bff_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE inklink_bff_app LOGIN PASSWORD :'app_password';
CREATE DATABASE inklink_bff OWNER inklink_bff_owner;
REVOKE CONNECT ON DATABASE inklink_bff FROM PUBLIC;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
GRANT CONNECT ON DATABASE inklink_bff TO inklink_bff_owner, inklink_bff_app;
\connect inklink_bff
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA bff AUTHORIZATION inklink_bff_owner;
GRANT USAGE ON SCHEMA bff TO inklink_bff_app;
ALTER DEFAULT PRIVILEGES FOR ROLE inklink_bff_owner IN SCHEMA bff
 GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inklink_bff_app;
SQL
