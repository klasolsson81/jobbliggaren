#!/usr/bin/env bash
#
# Reads Search Console aggregates for jobbliggaren.se and writes them to the job summary.
#
# GOVERNANCE. This read path is outside ADR 0166 D10 trigger 5 only as the ADR 0166 amendment for it pins it, and
# triggers 13 and 14 there reopen it. ADR 0177 is the design and holds the review rule: security-auditor reads every
# change to this script, its test, its samples and the workflow, and every bump of an action the job runs, before merge.
#
# Run by .github/workflows/search-console-read.yml, on main only. The calls are fixed: one permission check, five
# searchAnalytics.query requests with no dimensions (four page types and the site total), and two sitemaps.list
# requests.
#
# FAIL-CLOSED. A response is accepted only in the shapes checked below. Anything else prints one fixed line, naming the
# request and a class from a closed set (http <code>, permission, shape, keys, rows, path), and fails the job. Never a
# value and never a body: jq's stderr is discarded because its diagnostics can echo input, and responses live only
# under $RUNNER_TEMP and are removed on exit.

set -euo pipefail

readonly API='https://www.googleapis.com/webmasters/v3/sites/sc-domain%3Ajobbliggaren.se'
readonly INDEX_PARAM='sitemapIndex=https%3A%2F%2Fjobbliggaren.se%2Fsitemap.xml'

# The request bodies. No body carries `dimensions`: with none, Google combines everything into one row without keys,
# so no query and no page value can come back. Google forbids `byProperty` with a page filter, hence `auto` there.
readonly BODY_HOME='{"startDate":"@START@","endDate":"@END@","type":"web","dataState":"final","aggregationType":"auto","dimensionFilterGroups":[{"groupType":"and","filters":[{"dimension":"page","operator":"includingRegex","expression":"^https://jobbliggaren\\.se/$"}]}]}'
readonly BODY_JOB='{"startDate":"@START@","endDate":"@END@","type":"web","dataState":"final","aggregationType":"auto","dimensionFilterGroups":[{"groupType":"and","filters":[{"dimension":"page","operator":"includingRegex","expression":"^https://jobbliggaren\\.se/jobb/"}]}]}'
readonly BODY_CATALOGUE='{"startDate":"@START@","endDate":"@END@","type":"web","dataState":"final","aggregationType":"auto","dimensionFilterGroups":[{"groupType":"and","filters":[{"dimension":"page","operator":"includingRegex","expression":"^https://jobbliggaren\\.se/lediga-jobb(?:[/?]|$)"}]}]}'
readonly BODY_OTHER='{"startDate":"@START@","endDate":"@END@","type":"web","dataState":"final","aggregationType":"auto","dimensionFilterGroups":[{"groupType":"and","filters":[{"dimension":"page","operator":"includingRegex","expression":"^https://jobbliggaren\\.se/."},{"dimension":"page","operator":"excludingRegex","expression":"^https://jobbliggaren\\.se/(?:jobb/|lediga-jobb(?:[/?]|$))"}]}]}'
readonly BODY_SITE='{"startDate":"@START@","endDate":"@END@","type":"web","dataState":"final","aggregationType":"byProperty"}'

if [ -z "${SEARCH_CONSOLE_ACCESS_TOKEN:-}" ] || [ -z "${RUNNER_TEMP:-}" ] || [ -z "${GITHUB_STEP_SUMMARY:-}" ]; then
  echo "::error::search-console-read: SEARCH_CONSOLE_ACCESS_TOKEN, RUNNER_TEMP and GITHUB_STEP_SUMMARY must be set."
  exit 1
fi

# The window: the latest calendar month whose last day is at least four days before the run (UTC), so every run in a
# month prints the same final numbers. SEARCH_CONSOLE_RUN_DATE exists for the fixture suite; the workflow never sets it.
run_date=${SEARCH_CONSOLE_RUN_DATE:-$(date -u +%F)}
if ! [[ $run_date =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || ! date -u -d "$run_date" +%F >/dev/null 2>&1; then
  echo "::error::search-console-read: the run date is not a date."
  exit 1
fi
last_prev=$(date -u -d "${run_date:0:7}-01 -1 day" +%F)
if [ $(( ($(date -u -d "$run_date" +%s) - $(date -u -d "$last_prev" +%s)) / 86400 )) -ge 4 ]; then
  END=$last_prev
else
  END=$(date -u -d "${last_prev:0:7}-01 -1 day" +%F)
fi
START="${END:0:7}-01"
readonly START END

WORK=$(mktemp -d "$RUNNER_TEMP/search-console.XXXXXX")
trap 'rm -rf "$WORK"' EXIT

# The only jq call site. Its diagnostics can echo input values, so its stderr never reaches the log.
q() { jq "$@" 2>/dev/null; }

refuse() { # refuse <request> <class>
  echo "::error::search-console-read refused: $1 $2"
  printf '**Search Console:** refused: `%s` %s. No value is printed; see ADR 0177.\n' "$1" "$2" >>"$GITHUB_STEP_SUMMARY"
  exit 1
}

# The only network call. The method is a constant at each call site. The token reaches curl on stdin as a config line,
# never in argv. No -v, no --trace, no --fail-with-body: nothing of a response reaches a log.
call() { # call <request> <GET|POST> <url> [<body file>]; leaves the response in $RESP
  local file=${1//[^a-z]/-} code
  RESP="$WORK/$file.json"
  local args=(-q --proto '=https' --silent --max-time 30 --request "$2" --output "$RESP" --write-out '%{http_code}' --config -)
  if [ "$#" -eq 4 ]; then
    args+=(--header 'Content-Type: application/json' --data-binary "@$4")
  fi
  code=$(printf 'header = "Authorization: Bearer %s"\n' "$SEARCH_CONSOLE_ACCESS_TOKEN" \
    | curl "${args[@]}" "$3" 2>/dev/null) || code=000
  [[ $code =~ ^[0-9]{3}$ ]] || code=000
  [ "$code" = 200 ] || refuse "$1" "http $code"
  [ "$(q -s length "$RESP")" = 1 ] || refuse "$1" shape
}

# 1. The permission check. Restricted is the only limit Google enforces; with `webmasters.readonly` the scope is ours.
call sites.get GET "$API"
level=$(q -r 'if type == "object" and (.permissionLevel | type) == "string" then .permissionLevel else "" end' \
  "$RESP") || level=
case "$level" in
  siteRestrictedUser) ;;
  "") refuse sites.get shape ;;
  *) refuse sites.get permission ;;
esac

# 2. One row per page type. Accepted: `rows` absent (no data), or exactly one row whose `keys` is absent or empty and
# whose four values are numbers.
rows=""
for type in home job catalogue other site; do
  case "$type" in
    home) body=$BODY_HOME ;;
    job) body=$BODY_JOB ;;
    catalogue) body=$BODY_CATALOGUE ;;
    other) body=$BODY_OTHER ;;
    site) body=$BODY_SITE ;;
  esac
  body=${body//@START@/$START}
  body=${body//@END@/$END}
  printf '%s' "$body" >"$WORK/body-$type.json"
  call "query:$type" POST "$API/searchAnalytics/query" "$WORK/body-$type.json"
  verdict=$(q -r '
    def agg: (.responseAggregationType // null) as $a
      | $a == null or ($a | type) == "string" and ($a == "auto" or $a == "byPage" or $a == "byProperty");
    if type != "object" or (agg | not) then "shape"
    elif has("rows") | not then "none"
    elif (.rows | type) != "array" or (.rows | length) == 0 then "shape"
    elif (.rows | length) > 1 then "rows"
    elif (.rows[0] | type) != "object" then "shape"
    elif (.rows[0] | has("keys")) and ((.rows[0].keys | type) != "array" or (.rows[0].keys | length) > 0) then "keys"
    elif all(.rows[0].clicks, .rows[0].impressions, .rows[0].ctr, .rows[0].position; type == "number") then "one"
    else "shape" end' "$RESP") || verdict=shape
  case "$verdict" in
    none) rows+="| $type | – | – | – | – | $(q -r '.responseAggregationType // "–"' "$RESP") |"$'\n' ;;
    one) rows+=$(q -r --arg t "$type" '
      .rows[0] as $r
      | "| \($t) | \($r.clicks | round) | \($r.impressions | round) | \(($r.ctr * 10000 | round) / 100) % | \(($r.position * 10 | round) / 10) | \(.responseAggregationType // "–") |"
      ' "$RESP")$'\n' ;;
    *) refuse "query:$type" "$verdict" ;;
  esac
done

# 3. The sitemaps: the property's own list, and the index's children. A path outside our sitemap pattern is refused
# before anything of the list is read further.
sitemaps=""
for request in sitemaps.list sitemaps.list:index; do
  url="$API/sitemaps"
  [ "$request" = sitemaps.list ] || url="$url?$INDEX_PARAM"
  call "$request" GET "$url"
  verdict=$(q -r '
    def count: . == null or (type == "number" and . >= 0 and . == floor) or (type == "string" and test("^[0-9]{1,12}\\z"));
    def entry: type == "object"
      and ((.lastDownloaded // null) | . == null or (type == "string" and test("^[0-9]{4}-[0-9]{2}-[0-9]{2}")))
      and ((.isPending // null) | . == null or type == "boolean")
      and (.errors | count) and (.warnings | count)
      and ((.contents // []) | type == "array" and all(.[]; type == "object" and (.type | type) == "string" and (.submitted | count)));
    if type != "object" then "shape"
    elif has("sitemap") | not then "none"
    elif (.sitemap | type) != "array" then "shape"
    elif any(.sitemap[]; type != "object" or (.path | type) != "string") then "shape"
    elif any(.sitemap[]; .path | test("^https://jobbliggaren\\.se/sitemap[-a-z0-9]*\\.xml\\z") | not) then "path"
    elif all(.sitemap[]; entry) then "ok"
    else "shape" end' "$RESP") || verdict=shape
  case "$verdict" in
    none) ;;
    ok) sitemaps+=$(q -r '
      def show: if . == null then "–" else tostring end;
      .sitemap[]
      | "| \(.path | sub("^https://jobbliggaren\\.se/"; "")) | \((.lastDownloaded // null) | if . == null then "–" else .[0:10] end) | \(.isPending | show) | \(.errors | show) | \(.warnings | show) | \([(.contents // [])[] | select(.type == "web") | .submitted][0] | show) |"
      ' "$RESP")$'\n' ;;
    *) refuse "$request" "$verdict" ;;
  esac
done

{
  printf '### Search Console: %s to %s (final data)\n\n' "$START" "$END"
  printf 'Permission: `siteRestrictedUser`. Read with `docs/runbooks/search-console-read.md` §2.\n\n'
  printf '| Page type | Clicks | Impressions | CTR | Position | Aggregation |\n|---|---|---|---|---|---|\n%s\n' "$rows"
  printf 'Page types are aggregated by page and do not sum to the site total.\n\n'
  printf '### Sitemaps\n\n'
  if [ -n "$sitemaps" ]; then
    printf '| File | Last downloaded | Pending | Errors | Warnings | Submitted (web) |\n|---|---|---|---|---|---|\n%s\n' "$sitemaps"
  else
    printf 'No sitemap is listed for the property.\n'
  fi
} >>"$GITHUB_STEP_SUMMARY"

echo "::notice::search-console-read: accepted, permission siteRestrictedUser, window $START..$END"
