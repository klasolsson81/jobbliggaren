#!/usr/bin/env bash
# Classifies whether main carries commits no published image was built from.
#
# WHY THIS EXISTS. `release-images.yml` has no push trigger — automerge merges as a GitHub App
# and app-triggered events start no workflow runs — so its hourly `schedule` is the only
# automatic path to the box, and that path is the one measured to fail. `dotnet-architect`,
# 2026-08-30 on #1592: over ~79 h, ~79 expected scheduled runs and **11 actual (~14 %)**, with
# gaps reaching 13h18m. Three merged PRs (#1579, #1583, #1584) sat unbuilt until a manual
# dispatch built them; Klas found out by opening dev.jobbliggaren.se and seeing none of them.
#
# CLAUDE.md §6.5 answered that by making the dispatch the fourth close-out step, which is a
# checklist item with no mechanism behind it. This is the mechanism. `senior-cto-advisor`,
# 2026-08-30, chose it over a tighter cron on the rule both workflow headers already state —
# *"the only mechanism whose trigger survives the condition it exists to repair"*: a cron's
# trigger IS the thing measured to fail, while this runs at SessionStart and inherits none of
# that. (Extra builds would also deliver nothing: `jobbliggaren-reconcile.timer` pulls once an
# hour, so the box's intake caps below what one working cron line already asks for.)
#
# A PURE CLASSIFIER, ON PURPOSE. It takes the two SHAs as arguments and performs no network or
# `gh` call, so its whole behaviour is reachable from a fixture suite. The caller owns the
# measuring; this file owns the deciding, and only the deciding can be got wrong silently.
#
# Usage:   main-ahead-of-images.sh <main-tip-sha> <last-successful-build-sha>
# Prints:  "in-sync" | "ahead <built-short> <main-short>" | "not-measurable <reason>"
# Exit:    always 0 — a hygiene detector must never fail a session start.
set -u

main_tip="${1-}"
built="${2-}"

# Both inputs come from tools that report failure IN BAND: `gh ... --jq '.[0].headSha'` prints
# the string "null" when no run matched, and `git ls-remote` prints nothing when offline. Either
# would compare unequal to a real SHA and produce a confident, permanent false alarm. So the
# shape is checked before the comparison, and anything that is not a full lowercase hex SHA
# resolves to silence rather than to a warning.
is_sha() {
  case "$1" in
    "") return 1 ;;
    *[!0-9a-f]*) return 1 ;;
    *) [ "${#1}" -eq 40 ] ;;
  esac
}

if ! is_sha "$main_tip"; then
  echo "not-measurable main-tip"
  exit 0
fi

if ! is_sha "$built"; then
  echo "not-measurable last-build"
  exit 0
fi

if [ "$main_tip" = "$built" ]; then
  echo "in-sync"
  exit 0
fi

echo "ahead ${built%${built#????????}} ${main_tip%${main_tip#????????}}"
