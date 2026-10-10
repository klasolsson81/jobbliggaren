#!/usr/bin/env bash
#
# Shared by the host observation collectors (ADR 0157). Sourced, never executed: it defines
# functions and two constants and does nothing on its own.
#
# One collector publishes one JSON file, `<source>.json`, into OBSERVATIONS_DIR. The API
# container reads that directory through a read-only bind mount (deploy/docker-compose.yml, the
# api service), so a collector writes only metadata that is fit for any admin to read. The
# contract, the time semantics and the install steps are in docs/runbooks/host-observations.md.
#
# A collector chooses the BODY of the envelope and nothing else: either `"data":{...}` or
# `"error":"<token>"`. The schema number, the source name and `sampledAt` belong to this file, so
# a second collector cannot publish a different envelope shape.

readonly OBSERVATIONS_DIR=/run/jobbliggaren/observations
readonly OBSERVATION_SCHEMA=1
# The bracket expression holds `]` first and `-` last so both are literal members. A backslash, a
# space, a control character, a `$` and a backtick fall outside it.
readonly OBSERVATION_BODY_ALPHABET='^[]A-Za-z0-9:,{}"._[-]+$'

observe_log() { printf '%s\n' "$*" >&2; }

# The only timestamp form in the contract: UTC, whole seconds.
observe_now() { date -u +%Y-%m-%dT%H:%M:%SZ; }
observe_iso() { date -u -d "@$1" +%Y-%m-%dT%H:%M:%SZ; }

# observe_publish <source> <body>
#
# <body> is built by the collector from regex-validated tokens and timestamps. The allowlist
# below is the second line of defence: a backslash, a space or a control character cannot appear
# in the body.
observe_publish() {
  local source="$1" body="$2" tmp
  [[ "$source" =~ ^[a-z]+$ ]] || { observe_log "REFUSING: '$source' is not a source name"; return 1; }
  [[ "$body" =~ $OBSERVATION_BODY_ALPHABET ]] || { observe_log "REFUSING: the $source body holds a character outside the contract's alphabet"; return 1; }

  tmp=$(mktemp "${OBSERVATIONS_DIR}/.${source}.XXXXXX") || { observe_log "REFUSING: cannot create a file in ${OBSERVATIONS_DIR}"; return 1; }
  {
    printf '{"schema":%s,"source":"%s","sampledAt":"%s",%s}\n' \
      "$OBSERVATION_SCHEMA" "$source" "$(observe_now)" "$body" > "$tmp" \
      && chmod 0644 -- "$tmp" \
      && mv -f -- "$tmp" "${OBSERVATIONS_DIR}/${source}.json"
  } || { rm -f -- "$tmp"; observe_log "REFUSING: could not publish ${source}.json"; return 1; }
}

# observe_publish_error <source> <token>   — the collector could not establish the facts at all.
observe_publish_error() {
  [[ "$2" =~ ^[a-z]+(-[a-z]+)*$ ]] || { observe_log "REFUSING: '$2' is not an error token"; return 1; }
  observe_publish "$1" "\"error\":\"$2\""
}
