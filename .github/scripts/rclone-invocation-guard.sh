#!/usr/bin/env bash
# rclone-invocation-guard.sh — BLOCKING (#1289).
#
# WHAT IT PINS: every `rclone` invocation under `deploy/` uses only `rcat` or `cat`; no forbidden
# subcommand or flag appears anywhere; and the binary is never bound to a variable, which is how
# every position-based check gets defeated.
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
# SCOPE is DYNAMIC — every tracked `*.sh` AND `*.service` under `deploy/` — and that is
# deliberate where `seq-retention-duration-guard.sh` uses a literal list. The threat here is a
# NEW file starting to invoke rclone; a literal list would be silently bypassed by adding one.
# The price of a dynamic scope is that it can narrow, so there are TWO checks: empty (total
# disappearance) and a floor (partial narrowing — see MIN_SCOPED).
#
# `*.service` is in scope because a unit can invoke a binary directly. Measured 2026-08-30: every
# `ExecStart=` today points at a `.sh` under `deploy/systemd/`, so nothing depends on this — but
# `ExecStart=/usr/bin/rclone rcd --rc-no-auth` is a one-line unit file, and it was invisible to
# this guard until the scope included it. Rules B and D are plain regexes over lines and need no
# shell grammar, so the INI syntax costs nothing; measured green across all nine unit files.
#
# ⚠ WHY THERE ARE FOUR RULES AND NOT ONE, WHICH IS THE HISTORY WORTH KEEPING. The first version
# of this file had a single command-position parser: split the line into segments, strip leading
# keywords, check the command word. `security-auditor` and `dotnet-architect` each built their
# own adversarial fixtures against it on 2026-08-30 and between them measured EIGHT idiomatic
# shell forms that passed GREEN while invoking a forbidden subcommand — `rcd`, `copyto` or
# `lsjson`, depending on the form — variable-bound binaries
# (`RCLONE="${RCLONE:-rclone}"`), `VAR=value` prefixes, wrapper commands (`timeout 30 rclone …`),
# `xargs`, and a `#` inside a string. Command position in shell is not a thing a parser can
# enumerate; every list of openers is a list someone can step outside of.
#
# So position is no longer what the enforcement rests on. Rules B and D are POSITION-FREE, and
# they are what holds; rule A remains because it produces the precise error message.
#
# RULE A (allowlist, command position): every segment whose command word is `rclone` must name a
# verb in ALLOWED_VERBS. Best-effort BY CONSTRUCTION — it strips keywords, `VAR=` prefixes and a
# list of wrapper commands, and wrapper commands are an open set. Its job is the good diagnostic,
# not the guarantee.
#
# RULE B (blocklist, anywhere): `rclone` ADJACENT to any of the 52 forbidden subcommands fails
# wherever it appears. No position to model, so none of the eight forms defeats it. This is the
# net, and it is why the blocklist is every subcommand rather than a handful.
#
# RULE C (flags, anywhere): `--links` and `--metadata` are forbidden outright in scoped files.
# They have no legitimate use here and both carry their own CVE class (CVE-2026-54572,
# CVE-2024-52522, CVE-2026-79783). Measured 2026-08-30: zero occurrences under `deploy/`.
#
# RULE D (binary binding, anywhere): assigning the rclone binary to a variable is refused
# outright. This closes the whole indirection class at its single entry point instead of chasing
# it — see the pattern's own comment for why refusing beats following.
#
# WHOLE-LINE COMMENTS ARE SKIPPED: lines under `deploy/` legitimately write "the rclone config is
# a credential", and a guard that fires on honest text gets switched off. INLINE comments are
# stripped for rule A only and kept for rules B, C and D — the reasoning, and the measurement
# that forced it, are at the strip site.
#
# EXIT: 0 the predicate holds · 1 it does not, or the scope is empty or below its floor
# (fail-closed) · 2 the rule-D pattern did not reach awk (see the BEGIN block).
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

# EVERY rclone subcommand except `rcat`, `cat` (the allowlist above) and `config`. This is rule
# B, and it is the NET: rule A can only see command positions it models, and the two reviews
# measured eight idiomatic forms that defeat that modelling (enumerated in the header). A
# blocklist keyed on `rclone` ADJACENT to a verb has no position to model and is not defeated by
# any of them.
#
# Regenerate the source list (rclone v1.75.0 ships 55 subcommands; this list is those 55 minus
# the three named above, so 52 — verify with `tr ' ' '\n' | grep -c .` after editing):
#   curl -sS "https://api.github.com/repos/rclone/rclone/contents/docs/content/commands?ref=v1.75.0" | jq -r '.[].name | select(startswith("rclone_")) | ltrimstr("rclone_") | rtrimstr(".md") | split("_")[0]' | sort -u
#
# `config` IS DELIBERATELY ABSENT: three lines under `deploy/` legitimately write `rclone config`
# as prose inside a `die` message ("It is the base64 OF an rclone config file"), and putting it in
# this list fires on all three. A guard that fires on honest text gets switched off.
#
# ⚠ THE COST IS REAL AND IS NOT COVERED ELSEWHERE. `config` is the one subcommand outside rule B's
# net, so it is caught only where rule A sees the command position. Measured 2026-08-30, the
# residual form is a `#` inside a string ahead of it — `echo "chan #ops" ; rclone config create`
# passes, because rule A's inline-comment strip truncates the line and rule B is not watching this
# verb. `config` carries no CVE among the 19 measured against 1.60.1, which is why this is
# accepted rather than closed; it is not a claim that the subcommand is unreachable.
# Measured 2026-08-30: with these 52, zero false positives across all scoped files.
readonly FORBIDDEN_VERBS='about archive authorize backend bisync check checksum cleanup completion convmv copy copyto copyurl cryptcheck cryptdecode dedupe delete deletefile gendocs gitannex gui hashsum link listremotes ls lsd lsf lsjson lsl md5sum mkdir mount move moveto ncdu nfsmount obscure purge rc rcd rmdir rmdirs selfupdate serve settier sha1sum size sync test touch tree version'

# Flags forbidden outright — see RULE C above.
readonly FORBIDDEN_FLAGS='--links --metadata'

# RULE D: binding the rclone binary to a variable. Measured 2026-08-30 by `security-auditor` and
# `dotnet-architect` independently: `RCLONE="${RCLONE:-rclone}"` then `"$RCLONE" rcd --rc-no-auth`
# passed BOTH other rules, because the literal `rclone` appears only in an assignment and never
# beside the verb. That is the three CRITICAL CVEs, green. The fix refuses the FORM rather than
# following the indirection — an alias-tracking parser is a different program, and shell can hide
# a binary behind arbitrarily many hops.
# Measured against all scoped files: zero occurrences, so refusing it costs nothing here.
# The closing character class is exactly quote, whitespace, `;`, `&`, `|` and end-of-line, and
# each inclusion and exclusion was measured rather than reasoned:
#   `;` `&` `|` — `RC=/usr/bin/rclone; "$RC" rcd` ends the assignment with a separator and
#                 slipped through a first version that accepted only quote/space/EOL.
#   NOT `)`     — admitting it fired on honest prose: backup.sh:292 writes the die message
#                 "(1=pg_dump 2=age 3=rclone)", where `=rclone` is followed by `)`.
#   NOT `_` `.` — this is what keeps `rclone_config=…` and `…/rclone.conf` out.
readonly BINARY_BINDING_RE='=["'"'"']?([^"'"'"'[:space:]]*/)?rclone(["'"'"']|[[:space:]]|[;&|]|$)|:-[[:space:]]*([^"'"'"'[:space:]}]*/)?rclone[}"'"'"']'

# Floor for the dynamic scope. `dotnet-architect` measured 2026-08-30 that the empty-scope check
# catches TOTAL disappearance but not PARTIAL narrowing: rename one `deploy/` script away from
# `.sh` and the guard goes green over 15 files with no signal. `build.yml` already carries this
# pattern for the same reason ("A GLOB THAT MATCHES NOTHING PASSES VACUOUSLY").
# RAISE this when scripts are added. LOWERING it requires measuring why a file left the scope —
# that measurement is the whole point of the floor.
readonly MIN_SCOPED=25

mapfile -t SCOPED < <(git ls-files -- 'deploy/' | grep -E '\.(sh|service)$' || true)

if [ "${#SCOPED[@]}" -eq 0 ]; then
  echo "::error::rclone-invocation-guard: no tracked *.sh or *.service under deploy/ — fail-closed. Either the scope moved or git ls-files failed; do not read this as a pass."
  exit 1
fi

if [ "${#SCOPED[@]}" -lt "$MIN_SCOPED" ]; then
  echo "::error::rclone-invocation-guard: scope narrowed to ${#SCOPED[@]} files, floor is $MIN_SCOPED — fail-closed. A script renamed out of *.sh leaves the scope silently, so this is a narrowing until measured otherwise. If a file was legitimately removed, lower MIN_SCOPED in the same commit and say which file and why."
  exit 1
fi

for f in "${SCOPED[@]}"; do
  if [ ! -f "$f" ]; then
    echo "::error::scoped path is tracked but absent from the working tree: $f — fail-closed."
    exit 1
  fi
done

echo "scope: ${#SCOPED[@]} tracked *.sh + *.service under deploy/"

status=0

for f in "${SCOPED[@]}"; do
  findings=$(
    awk -v allowed="$ALLOWED_VERBS" -v forbidden="$FORBIDDEN_VERBS" -v flags="$FORBIDDEN_FLAGS" -v binding="$BINARY_BINDING_RE" '
      BEGIN {
        split(allowed, a, " ");   for (i in a) ALLOW[a[i]] = 1
        split(forbidden, b, " "); for (i in b) DENY[b[i]] = 1
        split(flags, c, " ");     for (i in c) BADFLAG[c[i]] = 1
        # An UNSET awk variable is the empty string, and `line ~ ""` is TRUE for every line —
        # so a mis-wired -v would turn rule D from a check into a blanket failure. Caught
        # exactly that way while wiring this. Refuse loudly instead of guessing.
        if (binding == "") {
          print "FATAL: rule D pattern was not passed to awk" > "/dev/stderr"
          exit 2
        }
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

        # ---- RULE D: binding the rclone binary to a variable ----
        # Refuses the FORM. Following the indirection would mean tracking assignments across a
        # shell script, and a binary can hide behind arbitrarily many hops; refusing the one
        # shape that starts every such chain is total where a tracker would be best-effort.
        if (line ~ binding) {
          printf "%d\tBINDING\t-\t%s\n", NR, substr(line, 1, 140)
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

          # Strip, repeatedly and in any order, the three things that can sit between a command
          # position and the binary: shell keywords (`then rclone …`), VAR=value prefixes
          # (`RCLONE_CONFIG=x rclone …`) and wrapper commands (`timeout 30 rclone …`,
          # `xargs rclone …`). All three were measured passing green by `dotnet-architect`
          # 2026-08-30. This list is best-effort BY CONSTRUCTION — wrapper commands are an open
          # set — which is exactly why rule B above is a full blocklist rather than six verbs:
          # A gets the good error message, B is what actually holds.
          sub(/^[[:space:]]+/, "", s)
          while (match(s, /^(if|while|until|then|do|else|elif|exec|eval|sudo|time|command|nohup|env|xargs|nice|ionice|stdbuf|flock|timeout|!|not)[[:space:]]+/) ||
                 match(s, /^[A-Za-z_][A-Za-z0-9_]*=[^[:space:]]*[[:space:]]+/) ||
                 match(s, /^[0-9]+[smhd]?[[:space:]]+/)) {
            s = substr(s, RLENGTH + 1)
            sub(/^[[:space:]]+/, "", s)
          }

          # Accept a path-qualified binary too: `/usr/bin/rclone rcat …`.
          sub(/^[^[:space:]]*\//, "", s)

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
        BINDING)
          msg="this binds the rclone binary to a variable, which defeats every position-based check. Measured 2026-08-30 by two reviewers independently: assigning the binary and then calling it through the variable passed this guard GREEN while invoking rcd, which is the three CRITICAL CVEs. Call the binary by its name."
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
