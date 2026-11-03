#!/bin/sh
set -eu
psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 \
  --set=owner_password="$SCHEDULING_OWNER_PASSWORD" --set=app_password="$SCHEDULING_APP_PASSWORD" <<'SQL'
CREATE ROLE inklink_scheduling_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE inklink_scheduling_app LOGIN PASSWORD :'app_password';
CREATE DATABASE inklink_scheduling OWNER inklink_scheduling_owner;
REVOKE CONNECT ON DATABASE inklink_scheduling FROM PUBLIC;
GRANT CONNECT ON DATABASE inklink_scheduling TO inklink_scheduling_owner, inklink_scheduling_app;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
\connect inklink_scheduling
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA scheduling AUTHORIZATION inklink_scheduling_owner;
GRANT USAGE ON SCHEMA scheduling TO inklink_scheduling_app;
ALTER DEFAULT PRIVILEGES FOR ROLE inklink_scheduling_owner IN SCHEMA scheduling
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inklink_scheduling_app;
SQL
