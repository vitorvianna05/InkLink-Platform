#!/bin/sh
set -eu
psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 \
  --set=owner_password="$ENGAGEMENTS_OWNER_PASSWORD" --set=app_password="$ENGAGEMENTS_APP_PASSWORD" <<'SQL'
CREATE ROLE inklink_engagements_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE inklink_engagements_app LOGIN PASSWORD :'app_password';
CREATE DATABASE inklink_engagements OWNER inklink_engagements_owner;
REVOKE CONNECT ON DATABASE inklink_engagements FROM PUBLIC;
GRANT CONNECT ON DATABASE inklink_engagements TO inklink_engagements_owner, inklink_engagements_app;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
\connect inklink_engagements
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA engagements AUTHORIZATION inklink_engagements_owner;
GRANT USAGE ON SCHEMA engagements TO inklink_engagements_app;
ALTER DEFAULT PRIVILEGES FOR ROLE inklink_engagements_owner IN SCHEMA engagements
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inklink_engagements_app;
SQL
