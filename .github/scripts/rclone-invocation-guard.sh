#!/usr/bin/env bash
# rclone-invocation-guard.sh — BLOCKING (#1289).
#
# WHAT IT PINS: every `rclone` invocation under `deploy/` uses only `rcat` or `cat`, and none of
# the subcommands or flags whose CVE classes would otherwise become reachable appears at all.
#
# WHY THIS EXISTS, AND IT IS NOT A STYLE RULE. Measured 2026-08-30 against OSV
# (`github.com/rclone/rclone` @ `1.60.1`, the version `vps-deploy-stack.md` row 28 protocols for
# the box): 19 version-exact CVEs, three of them CRITICAL. The entire safety argument for running
# that binary is ONE SENTENCE — *we invoke only `rcat` and `cat`* — because all three CRITICALs
# require the RC daemon and every HIGH requires a subcommand or backend we never invoke. Before
# this file, nothing held that sentence. It was true by habit, and habit is not a control.
#
# ⚠ WHAT IT DOES NOT DO, AND THIS SENTENCE IS LOAD-BEARING: it pins THE REPO'S INVOCATION
# SURFACE, never the box. Green here means "no scoped file invokes a forbidden subcommand". It
# never means the box is unaffected, never means the CVEs are closed, and never means the
# installed version is current — the box can be upgraded, downgraded or drift with this guard
# green throughout, because nothing in CI can read it. Row 28a owns the version axis and names a
# human reader for it; this guard owns the axis the repo actually controls. Confusing the two
# would rebuild the original defect inside its own remedy (`security-auditor`, 2026-08-30:
# a CI guard "vaktar posten, inte lådan").
#
# WHY STATIC AND NOT A STUB. `deploy/systemd/jobbliggaren-backup.test.sh` already rejects an
# unknown verb in its `rclone` stub — but a stub only sees paths some fixture actually drives,
# and `jobbliggaren-logship.test.sh`'s stub carries no verb allowlist at all. The logship timers
# are the enabled, hourly ones, so the unguarded half was precisely the half that goes live
# first, and `jobbliggaren-logship.sh` calls that leg "THE ONLY LEG CARRYING DATA-SUBJECT
# PERSONAL DATA". Half-present enforcement that looks whole is worse than none.
#
# SCOPE is DYNAMIC — every tracked `*.sh` under `deploy/` — and that is deliberate where
# `seq-retention-duration-guard.sh` uses a literal list. The threat here is a NEW file starting
# to invoke rclone; a literal list would be silently bypassed by adding one. Fail-closed if the
# set comes back empty, which is the failure mode a dynamic scope brings with it.
#
# THE PARSER SPLITS INTO COMMAND SEGMENTS rather than matching "rclone at a command position"
# with one regex. A single regex has to enumerate the openers (`^`, `|`, `;`, `&&`, `$(`, `` ` ``,
# `{`) and silently misses the keyword ones — `then rclone rcd …` and `do rclone serve …` both
# read as ordinary text to it. Splitting first and stripping leading keywords afterwards has no
# such blind spot, and rule B below is the net under it either way.
#
# RULE A (allowlist, command position): every segment whose command word is `rclone` must name a
# verb in ALLOWED_VERBS. An unrecognised or absent verb FAILS — fail-closed, so a form the parser
# cannot vouch for is a red build and not a silent pass.
#
# RULE B (blocklist, anywhere): the token `rclone` followed by a forbidden verb fails wherever it
# appears, whether or not rule A recognised the position. B is not redundant with A: it is what
# catches a command position A's segment logic does not model.
#
# RULE C (flags, anywhere): `--links` and `--metadata` are forbidden outright in scoped files.
# They have no legitimate use here and both carry their own CVE class (CVE-2026-54572,
# CVE-2024-52522, CVE-2026-79783). Measured 2026-08-30: zero occurrences under `deploy/`.
#
# WHOLE-LINE COMMENTS ARE SKIPPED, and the reason is measured rather than defensive: six comment
# lines under `deploy/` legitimately write "the rclone config is a credential". `config` is
# therefore NOT in the blocklist — it is prose here, and an interactive subcommand no script
# runs. INLINE comments are stripped for rule A only, and kept for rules B and C — the reasoning
# is at the strip site, where the measurement that forced it also lives.
#
# EXIT: 0 the predicate holds · 1 it does not, or the scope came back empty (fail-closed).
#
# USAGE: rclone-invocation-guard.sh [root]
# With no argument it judges THE DELIVERY — this repo. The argument exists for the fixture suite,
# which builds throwaway git repos carrying the same paths; the suite's last cases run both forms
# against the real root so a workflow step and the fixtures cannot disagree.
set -euo pipefail

root="${1:-}"
if [ -z "$root" ]; then
  root=$(git rev-parse --show-toplevel)
fi
cd -- "$root"

# The two verbs the delivered scripts use. `rcat` writes stdin to a remote object; `cat` reads a
# remote object to stdout. Neither has a local write side, which is the second independent ground
# closing the "arbitrary write from an untrusted remote" class (`security-auditor`, 2026-08-30).
readonly ALLOWED_VERBS='rcat cat'

# Forbidden subcommands. Each names a CVE class measured against 1.60.1 on 2026-08-30:
#   rcd     — CVE-2026-41176 / -41179 / -49980, all three CRITICAL, unauthenticated RCE
#   serve   — CVE-2026-59733 / -71309 (restic), CVE-2026-79781 (s3)
#   mount   — local write surface
#   sync    — local write surface, and remote-driven deletion
#   copy    — remote-driven local write
#   archive — CVE-2026-59732, S3 prefix escape via crafted archive paths
# `config` is deliberately ABSENT: it appears in prose in six comment lines under deploy/ and is
# an interactive subcommand no unit runs. Listing it would fire on honest text, and a guard that
# fires on honest text gets switched off.
readonly FORBIDDEN_VERBS='rcd serve mount sync copy archive'

# Flags forbidden outright — see RULE C above.
readonly FORBIDDEN_FLAGS='--links --metadata'

mapfile -t SCOPED < <(git ls-files -- 'deploy/' | grep '\.sh$' || true)

if [ "${#SCOPED[@]}" -eq 0 ]; then
  echo "::error::rclone-invocation-guard: no tracked *.sh under deploy/ — fail-closed. Either the scope moved or git ls-files failed; do not read this as a pass."
  exit 1
fi

for f in "${SCOPED[@]}"; do
  if [ ! -f "$f" ]; then
    echo "::error::scoped path is tracked but absent from the working tree: $f — fail-closed."
    exit 1
  fi
done

echo "scope: ${#SCOPED[@]} tracked *.sh under deploy/"

status=0

for f in "${SCOPED[@]}"; do
  findings=$(
    awk -v allowed="$ALLOWED_VERBS" -v forbidden="$FORBIDDEN_VERBS" -v flags="$FORBIDDEN_FLAGS" '
      BEGIN {
        split(allowed, a, " ");   for (i in a) ALLOW[a[i]] = 1
        split(forbidden, b, " "); for (i in b) DENY[b[i]] = 1
        split(flags, c, " ");     for (i in c) BADFLAG[c[i]] = 1
      }
      # Whole-line comments carry prose about the rclone config and are out of scope for every
      # rule. Inline comments are handled per-rule at the strip site below.
      /^[[:space:]]*#/ { next }

      {
        line = $0

        # Rule A reads the line with any INLINE comment removed; rules B and C read the whole
        # line. The split is deliberate and each half is load-bearing. Without it, an honest
        # `foo   # (rclone stub is restored below)` parses as a command segment naming the verb
        # "stub" and fails — measured on jobbliggaren-backup.test.sh:504 while writing this. But
        # stripping for B or C would let a forbidden verb hide behind a `#`, so B and C keep the
        # raw line and remain the fail-closed net under A.
        #
        # The strip is a heuristic: it also truncates at a `#` inside a quoted string. That costs
        # rule A some reach on such a line and nothing else, because B and C never see the strip.
        codeline = line
        sub(/[[:space:]]#.*$/, "", codeline)

        # ---- RULE C: forbidden flags anywhere on the line ----
        for (flag in BADFLAG) {
          if (index(line, flag) > 0) {
            printf "%d\tFLAG\t%s\t%s\n", NR, flag, substr(line, 1, 140)
          }
        }

        # ---- RULE B: `rclone <forbidden verb>` anywhere, regardless of position ----
        for (verb in DENY) {
          # ERE, no \b available: require a non-word char (or start) before rclone, and a
          # non-word char (or end) after the verb, so `rclone_config` and `rcatx` do not match.
          re = "(^|[^a-zA-Z0-9_])rclone[[:space:]]+" verb "([^a-zA-Z0-9_-]|$)"
          if (line ~ re) {
            printf "%d\tVERB\t%s\t%s\n", NR, verb, substr(line, 1, 140)
          }
        }

        # ---- RULE A: every command segment whose command word is rclone names an allowed verb ----
        # Split on shell command separators. `$(` is covered by the `(` in the class.
        n = split(codeline, seg, /[|;&(){}`]+/)
        for (k = 1; k <= n; k++) {
          s = seg[k]

          # Strip leading whitespace, then leading shell keywords, repeatedly: `then rclone …`,
          # `do sudo rclone …`. This is the blind spot a single command-position regex has.
          sub(/^[[:space:]]+/, "", s)
          while (match(s, /^(then|do|else|elif|exec|eval|sudo|time|!|not)[[:space:]]+/)) {
            s = substr(s, RLENGTH + 1)
            sub(/^[[:space:]]+/, "", s)
          }

          if (s !~ /^rclone[[:space:]]/) continue

          rest = substr(s, 8)
          sub(/^[[:space:]]+/, "", rest)

          # THE VERB MUST FOLLOW `rclone` IMMEDIATELY. rclone accepts global flags before the
          # subcommand, and parsing those would mean knowing which of its several hundred flags
          # take a separate value — a table that rots. All six delivered invocations put the verb
          # first, so the guard requires that shape, and reports the flag form as its own error
          # rather than mis-reporting the VALUE of that flag as an unknown subcommand.
          # (No apostrophes below this line inside the awk program: it is single-quoted, and one
          # apostrophe ends it — a syntax error the fixture suite caught immediately.)
          if (rest ~ /^-/) {
            printf "%d\tFLAGFIRST\t-\t%s\n", NR, substr(line, 1, 140)
            continue
          }

          if (rest == "") {
            printf "%d\tNOVERB\t-\t%s\n", NR, substr(line, 1, 140)
            continue
          }

          match(rest, /^[^[:space:]]+/)
          verb = substr(rest, 1, RLENGTH)

          if (!(verb in ALLOW)) {
            printf "%d\tNOTALLOWED\t%s\t%s\n", NR, verb, substr(line, 1, 140)
          }
        }
      }
    ' "$f"
  )

  if [ -n "$findings" ]; then
    status=1
    while IFS=$'\t' read -r ln kind tok text; do
      case "$kind" in
        FLAG)
          msg="forbidden flag '$tok'. It carries its own CVE class against the pinned version (CVE-2026-54572, CVE-2024-52522, CVE-2026-79783) and has no use in this repo."
          ;;
        VERB)
          msg="forbidden subcommand '$tok'. It opens a CVE class the reachability argument in vps-deploy-stack.md row 28a closes by never invoking it."
          ;;
        NOTALLOWED)
          msg="subcommand '$tok' is not in the allowlist ($ALLOWED_VERBS)."
          ;;
        NOVERB)
          msg="an rclone invocation with no subcommand this parser can read — fail-closed."
          ;;
        FLAGFIRST)
          msg="a global flag precedes the subcommand. Put the subcommand immediately after 'rclone' — the delivered form is: rclone rcat \"\${RCLONE_FLAGS[@]}\" ... — because parsing flags first would mean tracking which of rclone's several hundred flags take a value. This is a parse refusal, not a claim that the flag is unsafe."
          ;;
      esac
      echo "::error file=$f,line=$ln::$msg  ->  $text"
    done <<<"$findings"
  fi
done

if [ "$status" -eq 0 ]; then
  echo "OK — every rclone invocation under deploy/ uses only: $ALLOWED_VERBS"
else
  cat >&2 <<'REMEDY'

rclone-invocation-guard FAILED (#1289).

DO NOT simply widen the allowlist. The allowlist IS the safety argument: the box runs
rclone 1.60.1, which carried 19 version-exact CVEs when measured 2026-08-30 — three of them
CRITICAL — and every one of those is closed today only because we never invoke the subcommand
it needs. Debian ships no patched rclone in any suite (trixie 1.60.1, sid 1.69.3; fixes land in
1.74.4/1.75.0), so there is no apt remedy to fall back on.

If this change genuinely needs a new subcommand, the order is:
  1. Re-run the reachability measurement for that subcommand (the regeneration command is in
     docs/runbooks/vps-deploy-stack.md row 28a).
  2. Update row 28a with the result and its date.
  3. Have security-auditor grade it — the reachability verdict is hers, not this guard's.
  4. Only then change ALLOWED_VERBS, in the same PR as the row.
REMEDY
fi

exit "$status"
