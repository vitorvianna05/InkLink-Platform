#!/bin/sh
set -eu
rabbitmqctl await_startup --timeout 60 >/dev/null
rabbitmqctl import_definitions /opt/inklink/broker-definitions.json >/dev/null
if rabbitmqctl list_users -q | cut -f1 | grep -qx inklink_publisher; then
  rabbitmqctl change_password inklink_publisher "$RABBITMQ_PUBLISHER_PASS" >/dev/null
else
  rabbitmqctl add_user inklink_publisher "$RABBITMQ_PUBLISHER_PASS" >/dev/null
fi
if rabbitmqctl list_users -q | cut -f1 | grep -qx inklink_consumer; then
  rabbitmqctl change_password inklink_consumer "$RABBITMQ_CONSUMER_PASS" >/dev/null
else
  rabbitmqctl add_user inklink_consumer "$RABBITMQ_CONSUMER_PASS" >/dev/null
fi
rabbitmqctl set_permissions -p / inklink_publisher '^$' '^inklink\.engagements\.events\.v1$' '^$' >/dev/null
rabbitmqctl set_permissions -p / inklink_consumer '^$' '^inklink\.messaging\.retry\.v1$' '^inklink\.messaging\.opened\.v1$' >/dev/null
printf 'RabbitMQ service users configured.\n'
