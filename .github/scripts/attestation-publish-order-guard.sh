#!/usr/bin/env bash
#
# attestation-publish-order-guard.sh — nothing a consumer follows moves before the whole release is
# attested, and only the fan-in moves it (#1314, #1238, ADR 0149).
#
# usage:  attestation-publish-order-guard.sh [<release-images.yml> [<publish-release.sh>]]
#
# WHY THIS EXISTS. On 2026-08-11 a cell pushed `latest` and then failed at `Attest`, and the box
# refused its hourly apply for 56 minutes on an image our own pipeline had published (#1314). Since
# #1238 the order is longer and lives in two files: cells push and attest only the immutable
# `sha-<short>`; the fan-in job `publish` writes one record, attests it, verifies it blocking, and only
# then — in its `advance` step, through `publish-release.sh` — seals the record's name and moves
# `dev` and `latest`. Every link is a string in a file, and nothing in Actions, buildx or cosign checks
# any of them: a `docker push …:latest` added to a cell, an `if:` loosened, a checkout pointed at
# another ref, or a move called from `record` instead of `advance` all keep every job green.
#
# THE RULES, each with its own mutation case in the suite:
#   1. In the workflow file: `docker push` or `docker image push` of anything but `:sha-`, `docker build`
#      or `docker buildx build` with `--push`, `docker manifest push`, a `docker/build-push-action` step
#      whose `push:` is present and not `false`, any `imagetools create`, and crane, oras, skopeo and
#      regctl are refused.
#   2. The fan-in job `publish` needs exactly `[prepare, release]`, runs only on
#      `needs.prepare.outputs.publish == 'true'`, and carries the steps `record`, `attest`, `verified`
#      and `advance` in that order; `advance` is gated on exactly `steps.verified.outcome == 'success'`
#      and is the only step that calls `publish-release.sh advance`.
#   3. In `publish-release.sh`, `imagetools create` appears only inside `move_tag`, which only
#      `cmd_advance` calls; `docker push` appears only inside `push_image`, which only `cmd_record` (a
#      `pending-` tag) and `cmd_push` (a `sha-` tag) call.
#   4. In every run, every job's tree is `$GITHUB_SHA`, the commit the run's own identity names:
#      `prepare` checks out `github.sha`, `release` and `publish` check out
#      `needs.prepare.outputs.sha`, and all three assert it at run time in a step no `if:` gates.
#   5. The local-only tag `applied` is never pushed or moved by either file.
#   6. `concurrency.group` is the literal `release-images` with `cancel-in-progress: false`.
#   Plus, from #1314: in the cells, `attest` follows `push`, and `push` runs `publish-release.sh push`.
#
# THE EXIT CONTRACT IS THE HOUSE'S, and 2 never collapses into 1:
#   0 — the order holds.
#   1 — it does not: a declaration is wrong and the publish path is unsafe.
#   2 — could not answer: a file is missing or unreadable, or shaped in a way this reader does not
#       model. "I could not read it" must never read as "it holds".
#
# THE READER IS ANCHORED, and that is a limitation rather than a design win. Jobs are recognised at
# two spaces under `jobs:`, job keys at four, steps by `      - ` at six, step keys at eight, `with:`
# keys at ten — the indentation this workflow uses. A step or job written in another legal YAML shape
# is not read; the required ids then go missing and the guard refuses rather than passing.
#
# CRLF IS STRIPPED ON READ: the repo default is `core.autocrlf=true`, and a trailing `\r` would break
# every comparison below silently and in the passing direction.
set -euo pipefail

WORKFLOW="${1:-.github/workflows/release-images.yml}"
PUBLISHER="${2:-.github/scripts/publish-release.sh}"
readonly WORKFLOW PUBLISHER

readonly REQUIRED_NEEDS="[prepare, release]"
readonly REQUIRED_PUBLISH_IF="needs.prepare.outputs.publish == 'true'"
readonly REQUIRED_ADVANCE_IF="steps.verified.outcome == 'success'"
readonly REQUIRED_REF='${{ needs.prepare.outputs.sha }}'
readonly REQUIRED_PREPARE_REF='${{ github.sha }}'
readonly FOREIGN_TOOLS_RE='(^|[^A-Za-z0-9_-])(crane|oras|skopeo|regctl)([^A-Za-z0-9_-]|$)'
readonly COMMENT_RE='(^|[[:space:];&|()])#'

violations=0
violation() {
  echo "::error::attestation-publish-order-guard: $1" >&2
  shift
  for line in "$@"; do echo "  $line" >&2; done
  violations=$((violations + 1))
}
cannot_answer() {
  echo "::error::attestation-publish-order-guard: could not answer — $1" >&2
  exit 2
}

[ -f "$WORKFLOW" ] && [ -r "$WORKFLOW" ] || cannot_answer "no readable workflow: $WORKFLOW"
[ -f "$PUBLISHER" ] && [ -r "$PUBLISHER" ] || cannot_answer "no readable publisher script: $PUBLISHER"

# --- read the workflow into steps -----------------------------------------------------------------------
# Per step: job, index within the job, id, if, uses, checkout ref, run text (all lines of the block).
declare -a S_JOB S_ID S_IF S_USES S_REF S_RUN S_PUSH
declare -A JOB_NEEDS JOB_IF
section="" job="" n=-1 in_run=0 in_with=0
conc_group="" conc_cancel=""
while IFS= read -r raw || [ -n "$raw" ]; do
  line=${raw%$'\r'}
  case "$line" in
  "jobs:"*) section=jobs; job=""; continue ;;
  "concurrency:"*) section=concurrency; continue ;;
  [A-Za-z]*) section=other; continue ;;
  esac
  if [ "$section" = concurrency ]; then
    case "$line" in
    "  group: "*) conc_group=${line#  group: } ;;
    "  cancel-in-progress: "*) conc_cancel=${line#  cancel-in-progress: } ;;
    esac
    continue
  fi
  [ "$section" = jobs ] || continue
  if [[ $line =~ ^\ \ ([A-Za-z0-9_-]+):[[:space:]]*$ ]]; then
    job=${BASH_REMATCH[1]}
    in_run=0
    in_with=0
    continue
  fi
  [ -n "$job" ] || continue
  case "$line" in
  "    needs: "*) JOB_NEEDS[$job]=${line#    needs: } ; continue ;;
  "    if: "*) JOB_IF[$job]=${line#    if: } ; continue ;;
  esac
  if [[ $line == "      - "* ]]; then
    n=$((n + 1))
    S_JOB[$n]=$job
    S_ID[$n]=""
    S_IF[$n]=""
    S_USES[$n]=""
    S_REF[$n]=""
    S_RUN[$n]=""
    S_PUSH[$n]="<absent>"
    in_run=0
    in_with=0
    case "$line" in
    "      - uses: "*) S_USES[$n]=${line#      - uses: } ;;
    "      - run: "*) S_RUN[$n]=${line#      - run: } ;;
    esac
    continue
  fi
  [ "$n" -ge 0 ] && [ "${S_JOB[$n]}" = "$job" ] || continue
  if [ "$in_run" -eq 1 ]; then
    if [[ $line == "          "* || -z ${line// /} ]]; then
      S_RUN[$n]+=$'\n'"${line#          }"
      continue
    fi
    in_run=0
  fi
  if [ "$in_with" -eq 1 ]; then
    if [[ $line == "          "* ]]; then
      case "$line" in
      "          ref: "*) S_REF[$n]=${line#          ref: } ;;
      "          push: "*) S_PUSH[$n]=${line#          push: } ;;
      esac
      continue
    fi
    in_with=0
  fi
  case "$line" in
  "        id: "*) S_ID[$n]=${line#        id: } ;;
  "        if: "*) S_IF[$n]=${line#        if: } ;;
  "        uses: "*) S_USES[$n]=${line#        uses: } ;;
  "        with:"*) in_with=1 ;;
  "        run: |"* | "        run: >"*) in_run=1 ;;
  "        run: "*) S_RUN[$n]=${line#        run: } ;;
  esac
done <"$WORKFLOW"

[ "$n" -ge 0 ] || cannot_answer "read no steps at all from $WORKFLOW — the reader's anchoring no longer matches the file"
for j in prepare release; do
  found=0
  for i in "${!S_JOB[@]}"; do [ "${S_JOB[$i]}" = "$j" ] && found=1; done
  [ "$found" -eq 1 ] || cannot_answer "no steps read for job '$j'"
done

step_of() { # <job> <id> → index, or -1
  local i
  for i in "${!S_JOB[@]}"; do
    [ "${S_JOB[$i]}" = "$1" ] && [ "${S_ID[$i]}" = "$2" ] && { echo "$i"; return; }
  done
  echo -1
}

split_commands() { # <text> → SEGS: <text> with each command separator this reader knows turned into a newline
  SEGS=${1//&&/$'\n'}
  SEGS=${SEGS//||/$'\n'}
  SEGS=${SEGS//;/$'\n'}
  SEGS=${SEGS//|/$'\n'}
}

# --- rule 1 and 5: no mutable publish, no foreign registry tool, no `applied` — in the workflow --------
for i in "${!S_JOB[@]}"; do
  text=${S_RUN[$i]}
  [ -n "$text" ] || continue
  # Every physical line is read on its own, and a command that trailing backslashes continue over
  # several lines (the way a long build command is written) is read once more, joined. A `#` that
  # starts a word, quoted or not, is read as a comment: what follows it in that command is dropped,
  # and a line whose last command holds one does not continue.
  commands=() acc="" joined=0
  while IFS= read -r cmdline; do
    trimmed=${cmdline#"${cmdline%%[![:space:]]*}"}
    case "$trimmed" in "#"*) ;; *) commands+=("$cmdline") ;; esac
    split_commands "$cmdline"
    last=${SEGS##*$'\n'}
    if [[ $cmdline == *"\\" && ! $last =~ $COMMENT_RE ]]; then
      acc+="${cmdline%"\\"} "
      joined=$((joined + 1))
      continue
    fi
    [ "$joined" -eq 0 ] || commands+=("$acc$cmdline")
    acc="" joined=0
  done <<<"$text"
  [ "$joined" -eq 0 ] || commands+=("$acc")
  for cmdline in "${commands[@]}"; do
    split_commands "$cmdline"
    while IFS= read -r seg; do
      if [[ $seg =~ $COMMENT_RE ]]; then seg=${seg%%"${BASH_REMATCH[0]}"*}; fi
      case "$seg" in
      *"docker push"* | *"docker image push"*)
        case "$seg" in *":sha-"*) ;; *) violation "job '${S_JOB[$i]}' step '${S_ID[$i]:-<no id>}' pushes a mutable tag: ${seg# }" "Only :sha-<short> is pushed by the workflow; every mutable tag moves in publish-release.sh advance." ;; esac
        ;;
      esac
      if [[ $seg =~ docker([[:space:]]+buildx)?[[:space:]]+build([[:space:]]|$) && $seg =~ (^|[[:space:]])--push([[:space:]=]|$) ]]; then
        violation "job '${S_JOB[$i]}' step '${S_ID[$i]:-<no id>}' builds and pushes in one command (--push): ${seg# }"
      fi
      case "$seg" in *"docker manifest push"*) violation "job '${S_JOB[$i]}' step '${S_ID[$i]:-<no id>}' runs docker manifest push: ${seg# }" ;; esac
      case "$seg" in *"imagetools create"*) violation "job '${S_JOB[$i]}' step '${S_ID[$i]:-<no id>}' runs imagetools create" "Tag moves happen only in publish-release.sh move_tag, called from cmd_advance." ;; esac
      if [[ $seg =~ $FOREIGN_TOOLS_RE ]]; then violation "job '${S_JOB[$i]}' runs a registry tool this guard does not model: ${BASH_REMATCH[2]}"; fi
      case "$seg" in *":applied"*) violation "job '${S_JOB[$i]}' names the local-only tag :applied" ;; esac
    done <<<"$SEGS"
  done
done
for i in "${!S_JOB[@]}"; do
  case "${S_USES[$i]}" in
  docker/build-push-action@*)
    case "${S_PUSH[$i]}" in
    "<absent>" | false) ;;
    *) violation "job '${S_JOB[$i]}' step '${S_ID[$i]:-<no id>}' publishes from docker/build-push-action (push: ${S_PUSH[$i]})" "A build step loads the image; the push step pushes it, after the scan." ;;
    esac
    ;;
  esac
done

# --- the cells (#1314): attest follows push, and push pushes only :sha- ----------------------------------
p=$(step_of release push)
a=$(step_of release attest)
if [ "$p" -lt 0 ] || [ "$a" -lt 0 ]; then
  violation "the release job has no step with id 'push' or no step with id 'attest'"
elif [ "$a" -le "$p" ]; then
  violation "in the release job, 'attest' (step $a) does not follow 'push' (step $p)"
fi
if [ "$p" -ge 0 ] && [[ ${S_RUN[$p]} != *"publish-release.sh push "* ]]; then
  violation "the cells' push step does not push through publish-release.sh push" "Its push_image attests the digest the push produced, never a re-read of the tag."
fi

# --- rule 2: the fan-in ---------------------------------------------------------------------------------
if [ -z "${JOB_NEEDS[publish]+x}" ]; then
  violation "there is no fan-in job 'publish' with a needs: line" "Without it nothing waits for all five cells."
else
  [ "${JOB_NEEDS[publish]}" = "$REQUIRED_NEEDS" ] ||
    violation "the publish job's needs is '${JOB_NEEDS[publish]}', not '$REQUIRED_NEEDS'"
  [ "${JOB_IF[publish]:-}" = "$REQUIRED_PUBLISH_IF" ] ||
    violation "the publish job's if: is '${JOB_IF[publish]:-<none>}', not '$REQUIRED_PUBLISH_IF'" \
      "A status function there (always(), failure()) would run the fan-in over a failed or skipped cell."
  r=$(step_of publish record)
  at=$(step_of publish attest)
  v=$(step_of publish verified)
  ad=$(step_of publish advance)
  if [ "$r" -lt 0 ] || [ "$at" -lt 0 ] || [ "$v" -lt 0 ] || [ "$ad" -lt 0 ]; then
    violation "the publish job lacks one of the step ids record, attest, verified, advance"
  else
    [ "$r" -lt "$at" ] && [ "$at" -lt "$v" ] && [ "$v" -lt "$ad" ] ||
      violation "the publish job's steps are not in the order record → attest → verified → advance"
    [ "${S_IF[$ad]}" = "$REQUIRED_ADVANCE_IF" ] ||
      violation "the advance step's if: is '${S_IF[$ad]:-<none>}', not '$REQUIRED_ADVANCE_IF'"
    case "${S_USES[$at]}" in actions/attest-build-provenance@*) ;; *) violation "the publish job's attest step does not use actions/attest-build-provenance" ;; esac
    [[ ${S_RUN[$r]} == *"publish-release.sh record"* ]] || violation "the record step does not run publish-release.sh record"
    [[ ${S_RUN[$v]} == *"publish-release.sh verify"* ]] || violation "the verified step does not run publish-release.sh verify"
    [[ ${S_RUN[$ad]} == *"publish-release.sh advance"* ]] || violation "the advance step does not run publish-release.sh advance"
  fi
  for i in "${!S_JOB[@]}"; do
    [ "$i" = "${ad:--1}" ] && continue
    [[ ${S_RUN[$i]} == *"publish-release.sh advance"* ]] &&
      violation "job '${S_JOB[$i]}' step '${S_ID[$i]:-<no id>}' calls publish-release.sh advance; only the gated advance step may"
  done
fi

# --- rule 4: one tree ------------------------------------------------------------------------------------
for i in "${!S_JOB[@]}"; do
  case "${S_USES[$i]}" in actions/checkout@*) ;; *) continue ;; esac
  case "${S_JOB[$i]}" in
  prepare)
    [ "${S_REF[$i]}" = "$REQUIRED_PREPARE_REF" ] ||
      violation "prepare checks out '${S_REF[$i]:-<default ref>}', not $REQUIRED_PREPARE_REF" "The certificate names github.sha in every run; the tree holding the token must be that commit."
    ;;
  release | publish)
    [ "${S_REF[$i]}" = "$REQUIRED_REF" ] ||
      violation "job '${S_JOB[$i]}' checks out '${S_REF[$i]:-<default ref>}', not $REQUIRED_REF"
    ;;
  esac
done
for j in prepare release publish; do
  asserted=0
  for i in "${!S_JOB[@]}"; do
    [ "${S_JOB[$i]}" = "$j" ] && [ -z "${S_IF[$i]}" ] &&
      [[ ${S_RUN[$i]} == *'"$(git rev-parse HEAD)" = "$GITHUB_SHA"'* ]] && asserted=1
  done
  [ "$asserted" -eq 1 ] || violation "job '$j' never asserts, in a step no if: gates, that its tree is \$GITHUB_SHA"
done

# --- rule 6: one run at a time ----------------------------------------------------------------------------
[ "$conc_group" = release-images ] && [ "$conc_cancel" = false ] ||
  violation "concurrency is group '${conc_group:-<none>}', cancel-in-progress '${conc_cancel:-<none>}'; expected the literal release-images and false" \
    "Two overlapping publishers could each read dev as current and move it; the registry has no compare-and-swap."

# --- rule 3 and 5: the publisher script ---------------------------------------------------------------------
func="" in_move=0 creates_outside=0 move_calls_outside=0 move_calls=0 pushes_outside=0 push_calls_outside=0
while IFS= read -r raw || [ -n "$raw" ]; do
  line=${raw%$'\r'}
  if [[ $line =~ ^([a-z_]+)\(\)\ \{ ]]; then func=${BASH_REMATCH[1]}; continue; fi
  [ "$line" = "}" ] && { func=""; continue; }
  trimmed=${line#"${line%%[![:space:]]*}"}
  case "$trimmed" in "#"*) continue ;; esac
  code=${line%%#*}
  case "$code" in
  *"imagetools create"*) [ "$func" = move_tag ] || creates_outside=$((creates_outside + 1)) ;;
  esac
  if [[ $code =~ (^|[^a-z_])move_tag[[:space:]] ]]; then
    move_calls=$((move_calls + 1))
    [ "$func" = cmd_advance ] || move_calls_outside=$((move_calls_outside + 1))
  fi
  case "$code" in
  *"docker push"* | *"docker image push"*) [ "$func" = push_image ] || pushes_outside=$((pushes_outside + 1)) ;;
  esac
  if [[ $code =~ (^|[^a-z_])push_image[[:space:]] ]]; then
    case "$func" in
    cmd_record) [[ $code == *'"pending-'* ]] || violation "publish-release.sh cmd_record pushes something other than a pending- tag: ${code# }" ;;
    cmd_push) [[ $code == *'"sha-'* ]] || violation "publish-release.sh cmd_push pushes something other than a sha- tag: ${code# }" ;;
    *) push_calls_outside=$((push_calls_outside + 1)) ;;
    esac
  fi
  if [[ $code =~ $FOREIGN_TOOLS_RE ]]; then violation "publish-release.sh runs a registry tool this guard does not model: ${BASH_REMATCH[2]}"; fi
  case "$code" in *":applied"* | *" applied "*) violation "publish-release.sh names the local-only tag applied" ;; esac
done <"$PUBLISHER"
grep -q '^move_tag() {' "$PUBLISHER" || cannot_answer "publish-release.sh defines no move_tag(); the reader's anchoring no longer matches it"
grep -q '^cmd_advance() {' "$PUBLISHER" || cannot_answer "publish-release.sh defines no cmd_advance()"
grep -q '^push_image() {' "$PUBLISHER" || cannot_answer "publish-release.sh defines no push_image()"
[ "$creates_outside" -eq 0 ] || violation "publish-release.sh runs imagetools create outside move_tag ($creates_outside place(s))"
[ "$move_calls" -gt 0 ] || violation "publish-release.sh never calls move_tag; nothing would ever move"
[ "$move_calls_outside" -eq 0 ] || violation "publish-release.sh calls move_tag outside cmd_advance ($move_calls_outside place(s))"
[ "$pushes_outside" -eq 0 ] || violation "publish-release.sh runs docker push outside push_image ($pushes_outside place(s))"
[ "$push_calls_outside" -eq 0 ] || violation "publish-release.sh calls push_image outside cmd_record and cmd_push ($push_calls_outside place(s))"

if [ "$violations" -gt 0 ]; then
  exit 1
fi
echo "attestation-publish-order-guard: order holds"
echo "  cells push and attest :sha- only; publish runs record → attest → verified → advance;"
echo "  every tag move is publish-release.sh move_tag, called from cmd_advance alone."
