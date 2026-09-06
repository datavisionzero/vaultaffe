#!/bin/sh
# Put a backup back.
#
#   deploy/restore.sh <artifact> [--yes]
#
# This replaces the database of this installation and the `deploy/.env` beside
# it with what the artifact holds — both halves, because either one alone
# produces an instance that starts and cannot open a single value (ADR 0017).
# Anything the installation holds that is newer than the artifact is gone
# afterwards, and there is no undo: that is what a restore is, and it is asked
# about before it happens rather than mentioned afterwards.
#
# The stack does not have to be running. On a machine that has never run this
# product, the two commands are this one and `docker compose up -d`.
set -eu

umask 077

here=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)

compose() {
	docker compose -f "$here/docker-compose.yml" "$@"
}

artifact=
assume_yes=no
for argument in "$@"; do
	case $argument in
	--yes) assume_yes=yes ;;
	-*)
		echo "restore.sh: unknown option $argument" >&2
		exit 2
		;;
	*)
		if [ -n "$artifact" ]; then
			echo "restore.sh: one artifact, not two" >&2
			exit 2
		fi
		artifact=$argument
		;;
	esac
done

if [ -z "$artifact" ]; then
	echo "usage: restore.sh <artifact> [--yes]" >&2
	exit 2
fi
if [ ! -r "$artifact" ]; then
	echo "restore.sh: cannot read $artifact" >&2
	exit 2
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT INT TERM

# Unpacked before anything is asked or stopped, because this is where a file
# that is not what it was thought to be says so — while the installation is
# still exactly as it was.
if ! tar -xzf "$artifact" -C "$work" 2>/dev/null ||
	[ ! -f "$work/manifest" ] || [ ! -f "$work/database.dump" ] || [ ! -f "$work/env" ]; then
	echo "restore.sh: $artifact is not a vaultaffe backup" >&2
	exit 2
fi

# The first line is the format and its version. A later format will be a later
# number, and this is where an older script says so instead of restoring half of
# something it does not understand.
format=$(head -n 1 "$work/manifest")
if [ "$format" != "vaultaffe-backup 1" ]; then
	echo "restore.sh: $artifact says it is '$format', which this script does not know" >&2
	echo "            (a newer backup wants the restore.sh of the release that wrote it)" >&2
	exit 2
fi

field() {
	sed -n "s/^$1=//p" "$work/manifest"
}

echo "Artifact:  $artifact" >&2
echo "  taken:   $(field taken_at)" >&2
echo "  release: $(field release)" >&2
echo "  schema:  $(field schema)" >&2
echo >&2
echo "This replaces the database of the installation in $here and the deploy/.env" >&2
echo "beside it, including the master key. Whatever it holds now is gone." >&2

if [ "$assume_yes" != yes ]; then
	if [ ! -t 0 ]; then
		echo >&2
		echo "restore.sh: nothing to ask on — pass --yes if this is what you mean" >&2
		exit 1
	fi
	printf 'Type "restore" to go ahead: ' >&2
	read -r answer || answer=
	if [ "$answer" != restore ]; then
		echo "Nothing was changed." >&2
		exit 1
	fi
fi

stamp=$(date -u +%Y%m%dT%H%M%SZ)

# Stopped before anything is touched, so that the instance is not writing into a
# database that is about to be dropped, and does not reconnect to the empty one
# in between and migrate it.
compose stop vaultaffe caddy >/dev/null 2>&1 || true

# The configuration first, and the database after it: the cluster is created on
# the database container's first start from `POSTGRES_PASSWORD`, so on a machine
# with no data yet the password has to be the artifact's before that happens.
if [ -f "$here/.env" ] && ! cmp -s "$here/.env" "$work/env"; then
	mv "$here/.env" "$here/.env.replaced-$stamp"
	echo "The .env that was here is now .env.replaced-$stamp — it holds the old master key." >&2
fi
cp "$work/env" "$here/.env"

compose up -d --wait db

password=$(sed -n 's/^POSTGRES_PASSWORD=//p' "$here/.env")
if [ -z "$password" ]; then
	echo "restore.sh: the artifact's env has no POSTGRES_PASSWORD" >&2
	exit 1
fi

# An existing cluster keeps the password it was created with, whatever the
# environment of a later container start says — so a restore onto a machine that
# already ran an installation would leave the instance holding a password the
# database does not have. The artifact's configuration is the one that is going
# to be true in a moment, so the role is made to match it. `:'pw'` is psql
# quoting the value, which is the only safe way a password reaches SQL — and it
# is why the statement arrives on stdin: psql substitutes variables in what it
# reads and not in what `--command` hands it.
printf "alter role vaultaffe with password :'pw';\n" |
	compose exec -T db psql \
		--username vaultaffe --dbname postgres \
		--set ON_ERROR_STOP=1 --set pw="$password" >/dev/null

# Dropped rather than restored over: `pg_restore --clean` leaves behind whatever
# the artifact does not mention, and a table from a newer schema surviving into
# an older one is the kind of half-restore that is found much later.
compose exec -T db psql \
	--username vaultaffe --dbname postgres --set ON_ERROR_STOP=1 \
	--command 'drop database if exists vaultaffe with (force)' \
	--command 'create database vaultaffe owner vaultaffe' >/dev/null

compose exec -T db pg_restore \
	--username vaultaffe \
	--dbname vaultaffe \
	--single-transaction \
	--exit-on-error \
	<"$work/database.dump"

# And up. If the image is newer than the artifact, its migrations apply on this
# start, forward and by themselves, exactly as they do on an upgrade.
compose up -d --wait

echo >&2
echo "Restored. The instance is up; sign in and look at a secret you know the value of —" >&2
echo "that is the check that both halves of the artifact arrived." >&2
