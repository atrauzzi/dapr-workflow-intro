#!/bin/bash
#
# Turns the stock MongoDB container into a single-node replica set.
#
# Why this exists: Dapr Workflow persists its state through actors, and actor
# state writes use multi-document transactions. MongoDB only supports
# transactions on a replica set, so a standalone mongod fails with
# "using transactions with MongoDB requires connecting to a replica set".
#
# Aspire.Hosting.MongoDB has no replica-set helper, so we wrap the official
# entrypoint: mint a keyfile (mandatory once replication + auth are both on),
# initiate the set once, then hand off to the stock bootstrap.
set -euo pipefail

KEYFILE=/tmp/mongo-rs.key

# Internal-auth keyfile. A single-member set shares this secret with no one, so
# a fresh per-boot value is fine. mongod requires it be owned by, and readable
# only by, the mongodb user. base64 over /dev/urandom avoids an openssl dep.
head -c 756 /dev/urandom | base64 > "$KEYFILE"
chmod 400 "$KEYFILE"
chown mongodb:mongodb "$KEYFILE"

# Once the real (replSet-enabled) mongod accepts authenticated connections,
# initiate the set exactly once. rs.status() throws until initiated, so the
# loop naturally skips the entrypoint's transient standalone bootstrap instance
# and is a no-op on restarts where the set already exists.
(
  for _ in $(seq 1 60); do
    if mongosh --quiet --host 127.0.0.1 \
        -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" \
        --authenticationDatabase admin \
        --eval 'rs.status().ok' >/dev/null 2>&1; then
      exit 0
    fi
    if mongosh --quiet --host 127.0.0.1 \
        -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" \
        --authenticationDatabase admin \
        --eval 'rs.initiate({_id:"rs0",members:[{_id:0,host:"127.0.0.1:27017"}]})' >/dev/null 2>&1; then
      echo "[rs-init] replica set rs0 initiated"
      exit 0
    fi
    sleep 2
  done
  echo "[rs-init] WARNING: gave up waiting to initiate replica set rs0" >&2
) &

# The stock entrypoint creates the root user on a temporary standalone (it
# strips --replSet/--keyFile for that step), then execs this final command.
exec docker-entrypoint.sh mongod --replSet rs0 --keyFile "$KEYFILE" --bind_ip_all
