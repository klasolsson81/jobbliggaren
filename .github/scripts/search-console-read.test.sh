#!/usr/bin/env bash
# Fixtures for search-console-read.sh and the form of its workflow.
#
# Run:  bash .github/scripts/search-console-read.test.sh
#
# GOVERNANCE: this suite is in the file set ADR 0177's review rule covers (security-auditor reads every change before
# merge), and its allow-list below is the one security-auditor required (ADR 0166 amendment for this read path).
#
# No case reaches Google. `curl` is a stub first on PATH: it records each call's method, URL and body, checks that the
# token came on stdin as a config line, and answers with a sample from samples/search-console/ and a status code.
#
# Where the response samples come from (AGENTS.md §5 Tests). The accepted shapes follow Google's own reference pages,
# read 2026-10-18: the Sites resource (last updated 2024-07-23: `siteUrl`, `permissionLevel`), searchAnalytics.query
# (2026-08-11: `rows[]` with `keys`, `clicks`, `impressions`, `ctr`, `position`, and `responseAggregationType`; "If no
# dimensions are specified, all values will be combined into a single row") and the Sitemaps resource (2024-07-23:
# `path`, `lastDownloaded`, `isPending`, `errors`, `warnings`, `contents[].type` and `.submitted`; the reference types
# the counts only as `long`, so both a number and a string of digits are accepted). No real response is ever added:
# a response body never leaves the runner, so the first run's accepted outcome is the production evidence instead.
# The hostile samples (`*-hostile-*`) are unreachable under the fixed bodies, which carry no `dimensions` and name no
# job-ad page; their cases assert only that the refusal names its class and that nothing of the response is printed.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
SUT="$HERE/search-console-read.sh"
WF="$ROOT/.github/workflows/search-console-read.yml"
S="$HERE/samples/search-console"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
PASS=0; FAIL=0
readonly TOKEN="ya29.test-token-not-a-secret"
readonly MARK="HOSTILE-MARKER"
readonly API='https://www.googleapis.com/webmasters/v3/sites/sc-domain%3Ajobbliggaren.se'

ok()  { PASS=$((PASS+1)); echo "  ok   — $1"; }
bad() { FAIL=$((FAIL+1)); echo "  FAIL — $1"; echo "----- summary -----"; sed 's/^/    | /' "$TMP/summary.md" 2>/dev/null; echo "----- out -----"; printf '%s\n' "$OUT" | sed 's/^/    | /'; echo "-------------"; }
expect()       { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (got '$2', want '$3')"; fi; }
expect_has()   { case "$2" in *"$3"*) ok "$1" ;; *) bad "$1" ;; esac; }
expect_lacks() { case "$2" in *"$3"*) bad "$1" ;; *) ok "$1" ;; esac; }

# The curl stub. Any flag or flag value it does not know fails the call loudly, so a new flag in the script (a -v, a
# --trace, a second --header) cannot pass this suite unnoticed.
mkdir -p "$TMP/bin"
cat >"$TMP/bin/curl" <<'STUB'
#!/usr/bin/env bash
no() { echo "stub: unexpected argument" >>"$STUB/errors"; exit 99; }
method= url= out= data=
[ "${1-}" = -q ] || no
shift
while [ "$#" -gt 0 ]; do
  case "$1" in
    --request) case "$2" in GET|POST) method=$2 ;; *) no ;; esac; shift 2 ;;
    --output) out=$2; shift 2 ;;
    --data-binary) data=${2#@}; shift 2 ;;
    --proto) [ "$2" = '=https' ] || no; shift 2 ;;
    --max-time) [ "$2" = 30 ] || no; shift 2 ;;
    --write-out) [ "$2" = '%{http_code}' ] || no; shift 2 ;;
    --config) [ "$2" = - ] || no; shift 2 ;;
    --header) [ "$2" = 'Content-Type: application/json' ] || no; shift 2 ;;
    --silent) shift ;;
    https://*) url=$1; shift ;;
    *) no ;;
  esac
done
config=$(cat)
n=$(( $(cat "$STUB/count" 2>/dev/null || echo 0) + 1 )); echo "$n" >"$STUB/count"
printf '%s %s\n' "$method" "$url" >>"$STUB/calls"
if [ "$config" = "header = \"Authorization: Bearer $EXPECT_TOKEN\"" ]; then echo ok >>"$STUB/auth"; else echo bad >>"$STUB/auth"; fi
[ -z "$data" ] || cp "$data" "$STUB/body-$n"
code=$(cat "$STUB/resp/$n.code" 2>/dev/null || echo 200)
if [ "$code" = 000 ]; then printf '000'; exit 7; fi
cp "$STUB/resp/$n.json" "$out"
printf '%s' "$code"
STUB
chmod +x "$TMP/bin/curl"

# answer <n> <sample file> [<code>]: what the stub returns on call n
answer() { cp "$2" "$STUB/resp/$1.json"; [ -z "${3:-}" ] || echo "$3" >"$STUB/resp/$1.code"; }

# fresh: a new stub state with every call answered by the accepted samples
fresh() {
  STUB="$TMP/stub"; rm -rf "$STUB" "$TMP/runner" "$TMP/summary.md"; mkdir -p "$STUB/resp" "$TMP/runner"
  answer 1 "$S/response-sites-restricted.json"
  answer 2 "$S/response-query-one-row.json"
  answer 3 "$S/response-query-one-row-empty-keys.json"
  answer 4 "$S/response-query-no-rows.json"
  answer 5 "$S/response-query-one-row.json"
  answer 6 "$S/response-query-site.json"
  answer 7 "$S/response-sitemaps-property.json"
  answer 8 "$S/response-sitemaps-index.json"
}

# run: the script under the stub, with a fixed run date; sets RC, OUT (stdout and stderr) and SUMMARY
run() {
  OUT=$(PATH="$TMP/bin:$PATH" STUB="$STUB" EXPECT_TOKEN="$TOKEN" SEARCH_CONSOLE_ACCESS_TOKEN="$TOKEN" \
    RUNNER_TEMP="$TMP/runner" GITHUB_STEP_SUMMARY="$TMP/summary.md" SEARCH_CONSOLE_RUN_DATE="${RUN_DATE:-2026-10-18}" \
    bash "$SUT" 2>&1); RC=$?
  SUMMARY=$(cat "$TMP/summary.md" 2>/dev/null || true)
}
calls() { cat "$STUB/calls" 2>/dev/null | wc -l | tr -d ' '; }
nothing_leaked() { # nothing_leaked <case>
  expect_lacks "$1: no response value in the output"  "$OUT" "$MARK"
  expect_lacks "$1: no response value in the summary" "$SUMMARY" "$MARK"
  expect_lacks "$1: no token in the output"           "$OUT$SUMMARY" "$TOKEN"
}

echo "accepted: every call answered in an accepted shape"
fresh; run
expect      "exit 0"                              "$RC" 0
expect      "eight calls"                         "$(calls)" 8
expect      "the calls, in order" "$(cat "$STUB/calls")" "GET $API
POST $API/searchAnalytics/query
POST $API/searchAnalytics/query
POST $API/searchAnalytics/query
POST $API/searchAnalytics/query
POST $API/searchAnalytics/query
GET $API/sitemaps
GET $API/sitemaps?sitemapIndex=https%3A%2F%2Fjobbliggaren.se%2Fsitemap.xml"
i=2
for t in home job catalogue other site; do
  if cmp -s <(cat "$STUB/body-$i"; printf '\n') "$S/request-$t.json"; then ok "body $t is the golden file, byte for byte"; else bad "body $t differs from request-$t.json"; fi
  i=$((i+1))
done
expect      "no body carries dimensions"          "$(cat "$STUB"/body-* | grep -c dimensions)" 0
expect      "the token came on stdin, every call" "$(grep -c '^ok$' "$STUB/auth")" 8
expect      "the stub saw no unknown flag"        "$(cat "$STUB/errors" 2>/dev/null)" ""
expect_has  "the window"                          "$SUMMARY" "### Search Console: 2026-09-01 to 2026-09-30 (final data)"
expect_has  "the permission level"                "$SUMMARY" 'Permission: `siteRestrictedUser`.'
expect_has  "a page-type row"                     "$SUMMARY" "| home | 12 | 340 | 3.53 % | 8.4 | auto |"
expect_has  "empty keys are accepted"             "$SUMMARY" "| job | 12 | 340 | 3.53 % | 8.4 | auto |"
expect_has  "no rows is said, not hidden"         "$SUMMARY" "| catalogue | – | – | – | – | auto |"
expect_has  "the site total"                      "$SUMMARY" "| site | 15 | 512 | 2.93 % | 9.1 | byProperty |"
expect_has  "page types do not sum, said once"    "$SUMMARY" "do not sum to the site total"
expect_has  "the index row"                       "$SUMMARY" "| sitemap.xml | 2026-10-18 | false | 0 | 0 | – |"
expect_has  "a job part, counts as strings"       "$SUMMARY" "| sitemap-jobb-1.xml | 2026-10-18 | false | 0 | 0 | 21149 |"
expect_has  "a part not yet downloaded"           "$SUMMARY" "| sitemap-sidor.xml | – | true | – | – | 15 |"
expect_lacks "never the deprecated indexed count" "$SUMMARY" "999"
expect_has  "the outcome annotation"              "$OUT" "::notice::search-console-read: accepted, permission siteRestrictedUser, window 2026-09-01..2026-09-30"
expect      "the responses are gone"              "$(ls -A "$TMP/runner")" ""
nothing_leaked "accepted"

echo "permission: any level but Restricted stops before the first query"
for level in siteOwner siteFullUser siteUnverifiedUser; do
  fresh; jq --arg l "$level" '.permissionLevel = $l' "$S/response-sites-restricted.json" >"$STUB/resp/1.json"; run
  expect     "$level: exit 1"               "$RC" 1
  expect     "$level: one call"             "$(calls)" 1
  expect_has "$level: the class"            "$OUT" "::error::search-console-read refused: sites.get permission"
  expect_lacks "$level: the level is not printed" "$OUT$SUMMARY" "$level"
done
fresh; answer 1 "$S/response-sites-hostile-no-level.json"; run
expect_has  "no level is a shape refusal"         "$OUT" "refused: sites.get shape"
fresh; answer 1 "$S/response-hostile-two-documents.json"; run
expect_has  "two documents are a shape, never permission" "$OUT" "refused: sites.get shape"
expect      "two documents: one refusal line"     "$(printf '%s\n' "$OUT" | grep -c '::error::')" 1
nothing_leaked "two documents"
fresh; answer 2 "$S/response-hostile-empty.json"; run
expect_has  "an empty body is a shape"            "$OUT" "refused: query:home shape"

echo "http: a status other than 200, and a transport failure"
fresh; answer 1 "$S/response-sites-restricted.json" 403; run
expect      "403: exit 1"                         "$RC" 1
expect_has  "403: the class"                      "$OUT" "refused: sites.get http 403"
fresh; answer 3 "$S/response-hostile-body.json" 500; run
expect_has  "500 on a query"                      "$OUT" "refused: query:job http 500"
nothing_leaked "500"
fresh; echo 000 >"$STUB/resp/7.code"; run
expect_has  "transport failure is http 000"       "$OUT" "refused: sitemaps.list http 000"

echo "keys, rows, shape: the hostile query samples"
fresh; answer 2 "$S/response-query-hostile-keys.json"; run
expect      "keys: exit 1"                        "$RC" 1
expect_has  "keys: the class"                     "$OUT" "refused: query:home keys"
expect      "keys: no later call"                 "$(calls)" 2
nothing_leaked "keys"
fresh; answer 6 "$S/response-query-hostile-two-rows.json"; run
expect_has  "rows: the class"                     "$OUT" "refused: query:site rows"
nothing_leaked "rows"
fresh; answer 4 "$S/response-query-hostile-string-clicks.json"; run
expect_has  "a string value is a shape"           "$OUT" "refused: query:catalogue shape"
nothing_leaked "string value"
fresh; answer 5 "$S/response-query-hostile-truncated.json"; run
expect_has  "malformed JSON is a shape"           "$OUT" "refused: query:other shape"
expect_lacks "jq's diagnostic is not printed"     "$OUT" "jq:"
nothing_leaked "malformed"
fresh; answer 2 "$S/response-query-hostile-empty-rows.json"; run
expect_has  "an empty rows array is a shape"      "$OUT" "refused: query:home shape"
fresh; answer 2 "$S/response-query-hostile-aggregation.json"; run
expect_has  "an unknown aggregation type is a shape" "$OUT" "refused: query:home shape"
nothing_leaked "aggregation type"

echo "path, shape: the hostile sitemap samples"
fresh; answer 8 "$S/response-sitemaps-hostile-path.json"; run
expect_has  "a path outside the pattern"          "$OUT" "refused: sitemaps.list:index path"
nothing_leaked "path"
fresh; answer 7 "$S/response-sitemaps-hostile-negative-count.json"; run
expect_has  "a count that is not a count"         "$OUT" "refused: sitemaps.list shape"
fresh; answer 8 "$S/response-sitemaps-hostile-trailing-newline.json"; run
expect_has  "a path with a trailing newline"      "$OUT" "refused: sitemaps.list:index path"
fresh; answer 7 "$S/response-sitemaps-none.json"; answer 8 "$S/response-sitemaps-none.json"; run
expect      "no sitemap listed: exit 0"           "$RC" 0
expect_has  "no sitemap is said, not hidden"      "$SUMMARY" "No sitemap is listed for the property."

echo "a refusal writes no aggregate"
fresh; answer 8 "$S/response-sitemaps-hostile-path.json"; run
expect_lacks "no page-type table after a late refusal" "$SUMMARY" "| home |"

echo "the window"
for pair in 2026-10-18:2026-09-01..2026-09-30 2026-11-03:2026-09-01..2026-09-30 2026-11-04:2026-10-01..2026-10-31 \
            2027-01-02:2026-11-01..2026-11-30 2026-03-05:2026-02-01..2026-02-28 2028-03-05:2028-02-01..2028-02-29; do
  fresh; RUN_DATE=${pair%%:*} run
  expect_has "run ${pair%%:*} reads ${pair#*:}" "$OUT" "window ${pair#*:}"
done
fresh; RUN_DATE=2026-13-45 run
expect      "a run date that is not a date: exit 1" "$RC" 1
expect      "a run date that is not a date: no call" "$(calls)" 0

echo "preconditions"
fresh
OUT=$(PATH="$TMP/bin:$PATH" STUB="$STUB" RUNNER_TEMP="$TMP/runner" GITHUB_STEP_SUMMARY="$TMP/summary.md" bash "$SUT" 2>&1); RC=$?
expect      "no token: exit 1"                    "$RC" 1
expect      "no token: no call"                   "$(calls)" 0

echo "static: the endpoints and the form (security-auditor's allow-list, ADR 0166 amendment)"
# The complete set of https:// literals in the workflow and the script. A new host fails here until
# this list changes in the same diff, which the review rule then puts in front of security-auditor.
expected_literals='https://github.com/klasolsson81/jobbliggaren
https://jobbliggaren\\.se/
https://jobbliggaren\\.se/$
https://jobbliggaren\\.se/(?:jobb/|lediga-jobb(?:[/?]|$))
https://jobbliggaren\\.se/.
https://jobbliggaren\\.se/jobb/
https://jobbliggaren\\.se/lediga-jobb(?:[/?]|$)
https://jobbliggaren\\.se/sitemap[-a-z0-9]*\\.xml\\z
https://www.googleapis.com/auth/webmasters.readonly
https://www.googleapis.com/webmasters/v3/sites/sc-domain%3Ajobbliggaren.se'
expect      "the https literals are the allow-list" \
  "$(grep -ohE "https://[^\"'[:space:]]*" "$SUT" "$WF" | LC_ALL=C sort -u)" "$(printf '%s\n' "$expected_literals" | LC_ALL=C sort -u)"
expect      "no http:// literal"                  "$(grep -c 'http://' "$SUT" "$WF" | awk -F: '{s+=$2} END {print s}')" 0
code_lines() { grep -vE '^[[:space:]]*#' "$1"; }
expect      "one curl call site"                  "$(code_lines "$SUT" | grep -cE '(^|[^a-z_-])curl ')" 1
expect      "every method is GET or POST, literally" "$(code_lines "$SUT" | grep -oE '^[[:space:]]*call [^ ]+ [A-Z]+' | awk '{print $NF}' | sort -u | tr '\n' ' ')" "GET POST "
expect      "and every call site names one"       "$(code_lines "$SUT" | grep -cE '^[[:space:]]*call [^ ]+ ')" "$(code_lines "$SUT" | grep -cE '^[[:space:]]*call [^ ]+ (GET|POST) ')"
expect      "the endpoints built on the property" "$(code_lines "$SUT" | grep -oE '\$API[A-Za-z/]*' | LC_ALL=C sort -u | tr '\n' ' ')" '$API $API/searchAnalytics/query $API/sitemaps '
expect      "the index parameter"                 "$(code_lines "$SUT" | grep -cxF "readonly INDEX_PARAM='sitemapIndex=https%3A%2F%2Fjobbliggaren.se%2Fsitemap.xml'")" 1
expect      "no verbose, trace or body-on-fail flag" "$(code_lines "$SUT" | grep -cE -- '(^|[[:space:]])(-v|--verbose|--trace[a-z-]*|--fail-with-body|-x)([[:space:]]|$)|set -[a-z]*x')" 0
expect      "no debug annotation"                 "$(grep -c '::debug::' "$SUT" "$WF" | awk -F: '{s+=$2} END {print s}')" 0
expect      "one jq call site"                    "$(code_lines "$SUT" | grep -cE '(^|[^a-z_-])jq ')" 1
expect      "and it discards jq's stderr"         "$(code_lines "$SUT" | grep -E '(^|[^a-z_-])jq ' | grep -c '2>/dev/null')" 1

echo "static: the workflow's form (ADR 0177)"
wf=$(code_lines "$WF")
expect      "permissions: {} at the top"          "$(grep -c '^permissions: {}$' "$WF")" 1
expect      "the job's permissions, and only those" "$(printf '%s\n' "$wf" | awk '/^    permissions:$/ {f=1; next} f && /^      [a-z]/ {print; next} f {exit}' | tr -d ' ' | sort | tr '\n' ' ')" "id-token:write "
expect      "two triggers"                        "$(printf '%s\n' "$wf" | sed -n '/^on:$/,/^[a-z]/p' | grep -cE '^  [a-z_]+:')" 2
expect_has  "a schedule"                          "$wf" "  schedule:"
expect_has  "a dispatch"                          "$wf" "  workflow_dispatch:"
expect      "no inputs"                           "$(printf '%s\n' "$wf" | grep -c 'inputs:')" 0
expect      "one action, the auth action pinned by full SHA" "$(printf '%s\n' "$wf" | grep -cE '^ +(- )?uses: google-github-actions/auth@[0-9a-f]{40}( |$)')" 1
expect      "no other uses"                       "$(printf '%s\n' "$wf" | grep -c 'uses:')" 1
expect      "exactly three steps, two of them run" "$(printf '%s\n' "$wf" | grep -cE '^      - ') $(printf '%s\n' "$wf" | grep -cE '^ +run:')" "3 2"
for line in "GIT_CONFIG_NOSYSTEM: '1'" "GIT_CONFIG_GLOBAL: /dev/null" "GIT_TERMINAL_PROMPT: '0'" \
  '[[ $GITHUB_SHA =~ ^[0-9a-f]{40}$ ]] || { echo "::error::GITHUB_SHA is not a commit id"; exit 1; }' \
  'git fetch --quiet --no-tags --depth=1 https://github.com/klasolsson81/jobbliggaren "$GITHUB_SHA"' \
  '[ "$(git rev-parse HEAD)" = "$GITHUB_SHA" ] || { echo "::error::fetched $(git rev-parse HEAD), not $GITHUB_SHA"; exit 1; }' \
  'git diff --quiet HEAD -- .github/scripts/search-console-read.sh || { echo "::error::the script is not the commit'"'"'s"; exit 1; }'; do
  expect    "the fetch step: ${line:0:48}"        "$(printf '%s\n' "$wf" | grep -cF -- "$line")" 1
done
fetch_at=$(printf '%s\n' "$wf" | grep -nF 'git fetch ' | cut -d: -f1)
auth_at=$(printf '%s\n' "$wf" | grep -nF 'uses: google-github-actions/auth@' | cut -d: -f1)
expect      "the fetch comes before auth"         "$([ "${fetch_at:-0}" -gt 0 ] && [ "$fetch_at" -lt "${auth_at:-0}" ] && echo yes)" yes
expect      "no credential in the workflow"       "$(printf '%s\n' "$wf" | grep -cE 'GITHUB_TOKEN|github\.token|extraheader|secrets\.')" 0
expect_has  "no credentials file"                 "$wf" "create_credentials_file: false"
expect_has  "no exported variables"               "$wf" "export_environment_variables: false"
expect_has  "an access token"                     "$wf" "token_format: access_token"
expect_has  "the read-only scope"                 "$wf" "access_token_scopes: https://www.googleapis.com/auth/webmasters.readonly"
expect_has  "300 s"                               "$wf" "access_token_lifetime: 300s"
expect      "no continue-on-error"                "$(printf '%s\n' "$wf" | grep -c 'continue-on-error')" 0
expect      "one expression in the workflow"      "$(printf '%s\n' "$wf" | grep -c '\${{')" 1
expect      "and it is the token's env line"      "$(printf '%s\n' "$wf" | grep -c 'SEARCH_CONSOLE_ACCESS_TOKEN: \${{ steps.auth.outputs.access_token }}')" 1
expect      "the workflow never sets the run date" "$(grep -c 'SEARCH_CONSOLE_RUN_DATE' "$WF")" 0
expect      "a timeout of at most 10 minutes"     "$(printf '%s\n' "$wf" | grep -E '^ +timeout-minutes:' | awk '$2 <= 10' | wc -l | tr -d ' ')" 1
expect      "a GitHub-hosted runner"              "$(printf '%s\n' "$wf" | grep -c 'runs-on: ubuntu-latest')" 1
expect      "no placeholder identifier left"      "$(grep -cE 'PROJECT_NUMBER|PROJECT_ID|POOL_ID|PROVIDER_ID' "$WF")" 0

echo "static: Dependabot does not auto-merge the auth action (ADR 0177 D4)"
DA="$ROOT/.github/workflows/dependabot-automerge.yml"
expect      "the auth action's arm"               "$(grep -cF '*google-github-actions/auth*) eco_ok=false; auth_hit=true ;;' "$DA")" 1
expect      "the arm makes the PR ineligible"     "$(grep -A1 -F 'elif [[ "$auth_hit" == "true" ]]; then' "$DA" | grep -cF 'echo "eligible=false" >> "$GITHUB_OUTPUT"')" 1

echo
echo "search-console-read: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
