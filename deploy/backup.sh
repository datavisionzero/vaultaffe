#!/bin/sh
# A backup of this installation, as one file.
#
#   deploy/backup.sh [directory]
#
# It writes `vaultaffe-backup-<utc>.tar.gz` into the current directory, or into
# the one named, and prints the path. Nothing is scheduled and nothing is sent
# anywhere: when it runs, where the file goes and how long it is kept are the
# operator's, and this is the part that is easy to get wrong if it is prose.
#
# The file holds **both halves**: the database dump and `deploy/.env`, which
# carries the master key. That is the whole reason this is a script rather than
# two documented steps — a dump without the key restores an instance whose every
# secret is undecryptable, and the failure of two documented steps is that
# somebody performs one of them (ADR 0017). It also means the file is as
# sensitive as the installation itself; `docs/operations.md` says where it may
# then be put.
#
# What is deliberately *not* in it: Caddy's certificates. They are obtained
# again in seconds from the certificate authority, and a backup is for what
# cannot be fetched back.
set -eu

# 0600 on everything this script writes, including the artifact. A backup that
# arrived world-readable would have undone the reason it holds both halves.
umask 077

here=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)

compose() {
	docker compose -f "$here/docker-compose.yml" "$@"
}

into=${1:-.}
if [ ! -d "$into" ]; then
	echo "backup.sh: $into is not a directory" >&2
	exit 2
fi

if [ ! -f "$here/.env" ]; then
	echo "backup.sh: $here/.env does not exist — there is no key material to back up" >&2
	echo "           (an installation is started by copying .env.example to .env)" >&2
	exit 2
fi

# The database has to be up, and this script does not start it. Bringing an
# installation up as a side effect of backing it up would be a surprise on the
# one path where surprises are expensive.
if ! compose ps --services --status running | grep -qx db; then
	echo "backup.sh: the database is not running — start it first:" >&2
	echo "           docker compose -f $here/docker-compose.yml up -d db" >&2
	exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT INT TERM

# `pg_dump` runs *inside* the database container, so the tool is always the one
# that belongs to the server: a dump is refused by a client older than the
# server it reads, and pinning a client on the host would make upgrading
# Postgres a question about somebody's laptop.
compose exec -T db pg_dump \
	--username vaultaffe \
	--dbname vaultaffe \
	--format=custom \
	>"$work/database.dump"

cp "$here/.env" "$work/env"

# What produced it. A restore is the moment nobody wants to guess which build
# and which schema an artifact came from, and the answer costs two queries here.
# Both are best-effort: an instance that is down is a perfectly good moment to
# take a backup, and an unanswered question is worth less than a refusal.
release=$(compose exec -T vaultaffe wget --quiet --output-document=- \
	http://localhost:8080/api/handshake 2>/dev/null |
	sed -n 's/.*"release":"\([^"]*\)".*/\1/p') || release=
migration=$(compose exec -T db psql \
	--username vaultaffe --dbname vaultaffe --tuples-only --no-align \
	--command 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" desc limit 1' \
	2>/dev/null) || migration=
server=$(compose exec -T db psql \
	--username vaultaffe --dbname vaultaffe --tuples-only --no-align \
	--command 'show server_version' 2>/dev/null) || server=

taken_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
stamp=$(date -u +%Y%m%dT%H%M%SZ)

cat >"$work/manifest" <<EOF
vaultaffe-backup 1
taken_at=$taken_at
release=${release:-unknown}
schema=${migration:-unknown}
postgres=${server:-unknown}
EOF

artifact="$into/vaultaffe-backup-$stamp.tar.gz"
tar -czf "$artifact" -C "$work" manifest database.dump env

# The path on stdout and nothing else, so the artifact can be handed straight to
# whatever moves it off this machine. Everything a person reads goes to stderr.
echo "$artifact"
{
	echo "Backup of ${release:-an unknown release}, schema ${migration:-unknown}, taken $taken_at."
	echo "It contains the master key as well as the data. Treat it as you would the instance."
	echo "Restore it with: $here/restore.sh $artifact"
} >&2
