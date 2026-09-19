#!/bin/sh
set -eu

# Arguments are an identity and a secret-file path, never a credential value.
[ "$#" -eq 2 ] || exit 1
[ -s "$2" ] && [ -r "$2" ] || exit 1
response=$(redis-cli -e --user "$1" --askpass PING < "$2" 2>&1) || exit 1
[ "$response" = PONG ]
