#!/usr/bin/env bash
#
# Renders lhci's assertion results as two Markdown tables for the job summary:
# the byte budgets (`resource-summary`), then every other assertion (the timing).
#
# Usage:  lighthouse-summary.sh <assertion-results.json> <summary-file>
#
# A view, never a second judge: every value, budget, operator, level and verdict
# printed here is lhci's own, read from `.lighthouseci/assertion-results.json`. The
# script holds no URL list, no budget and no threshold. lhci writes passing rows to
# that file only under `assert.includePassedAssertions: true` (lighthouserc.json);
# without it a passing cell has no row to be read from and renders as "–".
#
# A missing, malformed or empty file appends one visible line to the summary and a
# ::warning::, never an empty or an all-green table. It still exits 0: the job is
# observe-only (ADR 0045 Beslut 6), so this step annotates and never gates.

set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: lighthouse-summary.sh <assertion-results.json> <summary-file>" >&2
  exit 2
fi
readonly IN=$1 OUT=$2

warn() {
  echo "::warning::Lighthouse summary: $1"
  printf '**Lighthouse:** %s\n\n' "$1" >>"$OUT"
}

if [ ! -f "$IN" ]; then
  warn "no assertion results at \`$IN\`."
  exit 0
fi

# Every field a cell is built from must be present with its type, or nothing renders.
if ! jq -e 'type == "array" and all(.[];
      (.url | type) == "string" and (.auditId | type) == "string"
      and (.name | type) == "string" and (.operator | type) == "string"
      and (.level | type) == "string" and (.passed | type) == "boolean"
      and (.actual | type) == "number" and (.expected | type) == "number"
      and ((.values // []) | type) == "array")' "$IN" >/dev/null 2>&1; then
  warn "\`$IN\` is not a list of lhci assertion results; nothing was rendered."
  exit 0
fi

if [ "$(jq 'length' "$IN")" -eq 0 ]; then
  warn "\`$IN\` holds no assertion results; nothing was evaluated."
  exit 0
fi

jq -r '
  def num: (. * 10000 | round) / 10000 | tostring;
  def mark: if .passed then "✓" elif .level == "warn" then "⚠" else "✗" end;
  def firsts: reduce .[] as $x ([]; if any(.[]; . == $x) then . else . + [$x] end);

  def table($rows; $runs):
    ($rows | map(._k) | unique) as $cols
    | ($rows | map(._p) | firsts) as $paths
    | ($cols | map(. as $c | $rows | map(select(._k == $c))) ) as $byCol
    | "| URL | "
      + ($byCol | map(
          (.[0] | (.auditProperty // .auditId) + (if .name == "maxNumericValue" then "" else " " + .name end))
          + " " + .[0].operator + " "
          + (map(.expected | num) | unique | join(" / "))
        ) | join(" | "))
      + " |\n|---|" + ($cols | map("---|") | join(""))
      + ($paths | map(. as $p
          | "\n| " + $p + " | "
            + ($cols | map(. as $c
                | ($rows | map(select(._p == $p and ._k == $c)) | first) as $r
                | if $r == null then "–"
                  else ($r.actual | num) + " " + ($r | mark)
                    + (if $runs and (($r.values // []) | length) > 0
                       then " (" + ($r.values | map(num) | join(" · ")) + ")"
                       else "" end)
                  end
              ) | join(" | "))
            + " |") | join(""));

  map(. + {
    _p: (.url | sub("^https?://[^/]+"; "") | if . == "" then "/" else . end | gsub("\\|"; "\\|")),
    _k: [.auditId, (.auditProperty // ""), .name]
  })
  | (map(select(.auditId == "resource-summary"))) as $bytes
  | (map(select(.auditId != "resource-summary"))) as $timing
  | "### Lighthouse: byte budgets\n\n"
    + (if ($bytes | length) > 0 then table($bytes; false) else "No `resource-summary` assertion in the results." end)
    + "\n\n### Lighthouse: timing\n\nRead per `docs/runbooks/performance-measurement.md` §F, D0.\n\n"
    + (if ($timing | length) > 0 then table($timing; true) else "No timing assertion in the results." end)
    + "\n\n✓ passed · ✗ failed (error) · ⚠ failed (warn) · – not evaluated. Each value is lhci'"'"'s aggregate over the runs; the runs follow in brackets.\n"
' "$IN" >>"$OUT"
