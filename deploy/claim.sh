#!/bin/sh
# This instance's claim secret, for whoever is standing on the machine.
#
#   deploy/claim.sh
#
# An unstarted instance is claimed by whoever completes its first run, and the
# first run needs the secret this prints (ADR 0019). The instance makes it
# itself and writes it to its own log at every start until somebody claims it,
# so this script is a convenience rather than the only way: `docker compose logs
# vaultaffe` says the same thing surrounded by everything else a start prints.
#
# The point of the whole arrangement is that reaching the port is not enough.
# Whoever can run this is on the machine, and that is the line it draws.
set -eu

here=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)

compose() {
	docker compose -f "$here/docker-compose.yml" "$@"
}

# Asked of the database rather than scraped out of the log. A log has every
# start in it and a person reading one has to know that the newest block is the
# one that counts; the row is unambiguous, and it is the same row the instance
# checks the first run against.
secret=$(compose exec -T db psql \
	--username "${POSTGRES_USER:-vaultaffe}" \
	--dbname "${POSTGRES_DB:-vaultaffe}" \
	--no-align --tuples-only \
	--command 'select secret from instance_claim order by created_at limit 1' 2>/dev/null || true)

secret=$(printf '%s' "$secret" | tr -d '[:space:]')

if [ -z "$secret" ]; then
	echo "This instance has no claim secret." >&2
	echo "It has been claimed already — somebody completed its first run — or it is not running." >&2
	echo "Check with: docker compose -f $here/docker-compose.yml ps" >&2
	exit 1
fi

# The secret to stdout on its own line and everything a person reads to stderr,
# so that `deploy/claim.sh | pbcopy` copies the secret and nothing else.
echo "Paste this into the first-run page, or pass it to \`vaultaffe instance start --claim-file\`." >&2
echo "It stops working the moment somebody claims this instance." >&2
printf '%s\n' "$secret"
