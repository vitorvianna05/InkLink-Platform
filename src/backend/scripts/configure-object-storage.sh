#!/bin/sh
# Cria, de forma idempotente, os buckets da decisão 0018 no SeaweedFS local.
# Permissões por serviço e leitura anônima ficam em object-storage-identities.json.
set -eu
master="${SEAWEEDFS_MASTER:-127.0.0.1:9333}"
existing="$(echo 's3.bucket.list' | weed shell -master="$master" 2>/dev/null || true)"
for bucket in inklink-catalog-media inklink-identity-media inklink-identity-private; do
  if printf '%s\n' "$existing" | grep -Eq "^[[:space:]]*${bucket}[[:space:]]"; then
    echo "bucket ${bucket} already exists"
  else
    result="$(echo "s3.bucket.create -name ${bucket}" | weed shell -master="$master" 2>&1)"
    printf '%s\n' "$result"
    printf '%s\n' "$result" | grep -q "created bucket ${bucket}" || { echo "failed to create ${bucket}" >&2; exit 1; }
  fi
done
