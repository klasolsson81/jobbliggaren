#!/usr/bin/env bash
#
# compose-loopback-guard.sh — every published port in a compose project must bind to loopback.
#
# WHY THIS EXISTS, and why a comment was not enough. Until #1198 the dev compose file bound
# five of six published ports to `0.0.0.0` — including a Seq instance running with
# authentication disabled, which held email bodies in plaintext — WHILE THE FILE'S OWN
# COMMENT STATED IT WAS LOOPBACK-BOUND. That comment was wrong for months and no reader
# caught it. Human reading is measured, on this exact file, not to cover this class.
#
# IT READS COMPOSE'S OWN RESOLVED MODEL, NOT THE FILE. The predecessor was a YAML parser
# written in awk, and four review rounds each produced one more legal spelling it passed
# silently — a quoted key, `? ports`, a flow sequence, a long-form mapping. `docker compose
# config` normalises every one of those to a single shape before the guard sees it, so the
# guard has no YAML parser and therefore no spelling to miss. That is the whole reason it
# exists in this form.
#
# WHAT THE REBUILD MADE STRICTER, measured 2026-08-04/05 against Compose v2.40.3. Shapes the
# awk parser could only REFUSE (exit 2 — the reader learns nothing) are now CAUGHT (exit 1,
# naming service and port): `? ports`, quoted keys, flow sequences, long-form mappings, and
# `[::]:`. `include:` and `extends:` were refused as PORTS-OUT-OF-VIEW because their ports
# live in another file; compose resolves both and inlines the ports, so that refusal class is
# gone and those ports are now checked. Host networking is the one refusal that stays: it
# publishes through no `ports:` key at all, so a ports check is structurally blind to it.
#
# HOST NETWORKING HAS TWO ROUTES AND THE OBVIOUS ONE IS NOT THE ONLY ONE. `network_mode: host`
# is the spelling everyone knows. The second is a service attached to a top-level network that
# resolves to the Docker network named `host` — `hostnet: {external: true, name: host}`, or
# just `host: {external: true}`, which compose fills in as `name: "host"`. That service gets
# host networking with **no `network_mode` key in its model at all**. Measured 2026-08-05, and
# measured against the awk predecessor too: it exits 0 on the same file, so this is a gap that
# predates the rewrite rather than one the rewrite opened. Closed here because this header
# claims host networking is refused, and a guard that overstates its coverage is the #1198
# defect. A network written `name: host` WITHOUT `external:` is refused on the same rule: the
# guard cannot certify what compose would do with a name that collides with the built-in.
#
# THE PREDICATE HAS TWO HALVES AND NEEDS BOTH:
#   - a bare `5435:5432`  -> the `host_ip` key is ABSENT (compose does not emit `0.0.0.0`)
#   - `0.0.0.0:5435:5432` -> `host_ip: "0.0.0.0"`, present and wide
# Checking only the value passes the first; checking only presence passes the second. So
# absence IS the violation, and so is a present-but-wide value.
#
# WHAT IT DECIDES AND WHAT IT REFUSES TO DECIDE — specific, not a universal:
#   IPv4 it decides in full. 127.0.0.0/8 is loopback (RFC 1122), so `127.0.0.2` passes; any
#   other dotted quad, `0.0.0.0` included, is a violation.
#   IPv6 it decides in exactly two spellings: `::1` passes and `::` is a violation. EVERY
#   OTHER IPv6 SPELLING IS REFUSED (exit 2), NOT JUDGED. Measured: compose strips the brackets
#   but does NOT normalise the address, so `[0:0:0:0:0:0:0:1]` arrives as
#   `0:0:0:0:0:0:0:1` — and that address IS loopback. Calling it "not bound to loopback" would
#   be asserting a fact this guard has not established, which is #1198's own defect one level
#   down. Deciding it needs IPv6 expansion the guard does not do, so it says so instead of
#   guessing. The refusal UNDER-claims on `[fe80::1]`, and that is the intended side of the
#   error: the refused class holds genuine loopback AND genuine wildcards (`[::0]`,
#   `[0:0:0:0:0:0:0:0]`, `[::ffff:0.0.0.0]` all land there, measured), and no member of it
#   exits 0. It costs precision, never permissiveness.
#
# DO NOT REPLACE THAT PARAGRAPH WITH A UNIVERSAL. The predecessor's header claimed "refuses
# every shape it does not model" three times and was false three times. When a shape gets
# through, refuse it and leave this paragraph specific.
#
# IT PROVES NOTHING ABOUT WHAT IS LISTENING. ADR 0050 `Amendment 2026-08-04` §5's point
# "Ingen container publicerar till 0.0.0.0" requires a `curl` from outside at cutover, and
# that requirement is untouched here. "Did the file change back?" and "what is running?" are
# different questions and neither substitutes for the other.
#
# IT JUDGES A COMPOSE PROJECT, NOT A COMPOSE FILE. Until this change it passed `-f <path>`,
# which SUPPRESSES compose's own file resolution — so it answered about an artefact that was
# not necessarily the one that runs. Two measured consequences, both older than the
# `docker compose config` rewrite (the awk predecessor exits 0 on both): `docker compose up`
# auto-loads a sibling `docker-compose.override.yml` that `-f` hides, and a committed
# `compose.yaml` OUTRANKS `docker-compose.yml` entirely — bare `docker compose config` then
# reads only the shadowing file while the guard reported OK on the one nobody runs. Both are
# closed by letting compose resolve the project: `--project-directory <dir> config`, no `-f`.
#
# SO THE ARGUMENT IS A DIRECTORY, AND THAT IS ENFORCED RATHER THAN DOCUMENTED. Measured
# 2026-08-05: `--project-directory` pointed at a FILE exits 0 — compose reads the path as a
# directory name, walks up to its parent and judges THAT project, naming it after the file
# (`docker-composeyml`). A caller still passing the old file-path interface would get a
# confident verdict about a different project rather than an error, so a non-directory is
# refused before compose is invoked.
#
# AND THE DIRECTORY MUST CARRY A COMPOSE FILE OF ITS OWN, because compose WALKS UP when it
# finds none. Measured the same day: `--project-directory <empty child>` resolved the
# PARENT's compose file, and a child holding only a `docker-compose.override.yml` resolved
# the parent's file while SILENTLY DROPPING the child's override. Either is a clean bill of
# health for a project nobody asked about — the defect this guard exists to close, one level
# up — so it checks for compose's four default base names itself and refuses when none is
# there. Without that check, project semantics would have opened a new false-clean path while
# closing two.
#
# WHAT IS STILL NAME-BASED IS WHICH PROJECTS IT IS POINTED AT. Inside a project it knows no
# service names and no port numbers, so a new service, port or rename is covered on arrival,
# and every file compose merges into that project is now covered with it. But a compose file
# in a directory nobody gates is still unjudged, and that is what the tripwire in the suite
# is for: it asserts the set of TRACKED compose files against a known list, so a new one
# turns the suite RED instead of arriving silently. It makes the file NOTICED, never JUDGED,
# and it matches on a NAME pattern, so a file called something else (`stack.yml`) is
# invisible to it, exactly as it is to compose itself.
#
# THE RESIDUAL THIS REBUILD INTRODUCES, named rather than left implicit: the answer now
# depends on a compose CLI VERSION as well as on the file. Every normalisation above is
# behaviour of the binary on the runner, and nothing in this repo pins it. That is a
# DIFFERENT risk from the one being retired (a parser that missed spellings), not a smaller
# version of it.
#
# AND IT IS DATED, NOT HYPOTHETICAL. Measured 2026-08-05 from actions/runner-images: the
# `ubuntu-latest` image (24.04) carries Docker Compose **2.38.2**, and the 26.04 image already
# in public preview carries **5.1.3** — a major version. `-latest` migrations are gradual and
# GitHub states they can change the OS under a workflow, so this repo will cross that boundary
# without editing a file. The measurements this guard was written against are Compose 2.40.3.
#
# WHAT CARRIES MOST OF THAT RISK IS FAIL-CLOSURE, NOT FIXTURE COVERAGE — ON THE PORTS AXIS.
# There, a model shape this guard does not recognise becomes a violation or a refusal, never
# a pass: rename or move `host_ip` and `has("host_ip")` goes false for EVERY entry, so every
# entry falls to NOT-LOOPBACK and the `clean` fixture falls with them; drop `config` or
# `--no-interpolate` and it is exit 2; change the JSON shape and it is exit 2. That is a
# property of the predicate and does not depend on the suite crossing the right thing.
#
# THE HOST-NETWORKING AXIS IS NOT FAIL-CLOSED AND MUST NOT BE READ AS IF IT WERE. Its two
# detections are NAME LOOKUPS — `network_mode == "host"`, and a network whose resolved `name`
# is `host` — so a compose release that stopped filling `name` in from the key, or renamed
# `network_mode`, would turn them into silent passes rather than refusals. The fixtures are
# the ONLY thing covering that axis.
#
# AND THAT WEAKNESS WAS PRESENT TENSE, NOT PROSPECTIVE, FOR TWO REVISIONS OF THIS PARAGRAPH.
# A literal comparison is defeated by a variable, and all three seats of the axis were
# measured exiting 0 while `up` gave host networking. They are refused now as
# UNRESOLVED-NETWORK-MODE/-NAME/-REF. What remains is the prospective risk above: a literal
# comparison against a value compose has RESOLVED. Two earlier versions of this paragraph
# were false — first without qualification, then by putting a live bypass in the future
# tense — which is why it now says which risk is which.
#
# THE FIXTURES MAKE THE DRIFT READABLE; THEY ARE NOT WHAT MAKES IT SAFE. Every normalisation
# this guard leans on is crossed by at least one fixture next to it — absent `host_ip` for a
# bare mapping, `0.0.0.0`/`::` for the wide forms, `[::1]` stripped to `::1`,
# `[0:0:0:0:0:0:0:1]` NOT normalised, `${VAR}` and a hostname left raw, ranges expanded per
# port, `include:`/`extends:` resolved, profile-gated services present without `--profile` —
# so an upgrade that changes one names itself instead of having to be diagnosed. A coverage
# argument is incomplete by construction: it does not reach a compose change no fixture
# crosses. Do not read it as a pin.
#
# PINNING THE RUNNER WAS CONSIDERED AND REJECTED, recorded here because it is the only part
# of this mechanism change that binds future work. A pinned `ubuntu-24.04` on one job while
# eleven others take `ubuntu-latest` rots into an incoherence, and GitHub retires images on a
# hard cutoff — so the pin converts a normal PR into a deadline-driven migration. It also
# freezes the unknown rather than removing it: the class worried about, a compose change no
# fixture crosses, exists identically under a pin. The measured cost of being wrong without
# one is a single red CI run, which is the signal you want.
#
# `--expect-min N` IS LOAD-BEARING. Shape alone cannot tell "all ports are loopback-bound"
# from "I found no ports". With no floor given, a run recognising zero entries is REFUSED;
# `--expect-min 0` is the only way to say zero is expected. THE UNIT CHANGED WITH THE
# `docker compose config` REWRITE: compose expands `8000-8002:8000-8002` into three entries
# where the awk parser counted one written list item, so the floor counts published ports and
# not written lines. THE SCOPE CHANGED WITH PROJECT SEMANTICS: it counts the ports of the
# resolved PROJECT, so a port an auto-loaded override adds counts too. Measured 2026-08-05,
# the repo root still resolves to 6 — it has no ranges and no override — so its floor is
# unaffected by both changes.
#
# Usage:  bash .github/scripts/compose-loopback-guard.sh [--expect-min N] [project-dir ...]
#         (defaults to the repository root)
#
# Exit 0 = every published port is loopback-bound and the floor is met.
# Exit 1 = a published port is not loopback-bound (service and port printed), OR the
#          --expect-min floor is not met. The second case names no port — the finding is an
#          absence.
#          THE PREFIX ON A FINDING IS THE PROJECT DIRECTORY THE GUARD WAS ASKED ABOUT, not
#          the file the entry is written in: a project resolves a base file, an auto-loaded
#          override, and everything reached through `include:`/`extends:`, and compose's
#          resolved model does not carry an entry's origin. Read it as "reachable from this
#          project", not as "written here".
# Exit 2 = the guard could not answer. Deliberately NOT folded into exit 1 — "the guard could
#          not run" must never read as "the guard passed". The classifier states the rule
#          rather than a list: ANYTHING that is not a read bind address is a refusal, so a
#          refusal class added later lands here without being remembered in a second place
#          (#1216). Today's members: a bad invocation, an argument that is not a directory or
#          carries no compose file of its own, a project compose refused, a missing tool, a
#          service on host networking, an entry compose left unresolved, and an IPv6 spelling
#          the guard will not judge.

set -euo pipefail

expect_min=""
args=()
while [ "$#" -gt 0 ]; do
  case "$1" in
    --expect-min)
      [ "$#" -ge 2 ] || { echo "compose-loopback-guard: --expect-min needs a value" >&2; exit 2; }
      expect_min=$2; shift 2
      case "$expect_min" in
        (*[!0-9]*|"") echo "compose-loopback-guard: --expect-min must be a non-negative integer" >&2; exit 2 ;;
      esac
      ;;
    # The `=` form is accepted because rejecting it was fail-closed but illegible: it fell
    # through to the argument loop and got described as the thing it is not — `no such file`
    # then, `not a directory` now. The illegibility outlived the message it was measured on.
    --expect-min=*)
      expect_min=${1#--expect-min=}; shift
      case "$expect_min" in
        (*[!0-9]*|"") echo "compose-loopback-guard: --expect-min must be a non-negative integer" >&2; exit 2 ;;
      esac
      ;;
    --) shift; args+=("$@"); break ;;
    *) args+=("$1"); shift ;;
  esac
done
set -- "${args[@]+"${args[@]}"}"

if [ "$#" -gt 0 ]; then
  dirs=("$@")
else
  script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
  repo_root=$(cd -- "$script_dir/../.." && pwd)
  dirs=("$repo_root")
fi

# COMPOSE'S FOUR DEFAULT BASE NAMES, in its own precedence order. Enumerated here rather than
# globbed because the property being checked is "compose resolves THIS directory", and that is
# true of exactly these four. An override alone does not stop the upward walk — measured, the
# parent's file wins and the child's override is dropped without a word.
compose_base_names=(compose.yaml compose.yml docker-compose.yaml docker-compose.yml)

for d in "${dirs[@]}"; do
  if [ ! -d "$d" ]; then
    echo "::error::compose-loopback-guard: not a directory: $d" >&2
    echo "The argument is a compose PROJECT DIRECTORY, not a compose file. A file path is" >&2
    echo "refused HERE because compose does not refuse it: it reads the path as a directory" >&2
    echo "name, walks up to the parent, and judges that project instead." >&2
    exit 2
  fi
  has_base=0
  for n in "${compose_base_names[@]}"; do
    if [ -f "$d/$n" ]; then has_base=1; break; fi
  done
  if [ "$has_base" -eq 0 ]; then
    echo "::error::compose-loopback-guard: no compose file in $d" >&2
    echo "Refused rather than answered. Compose walks UP when a directory carries none of" >&2
    echo "${compose_base_names[*]}, so the verdict would be about an ancestor's project." >&2
    exit 2
  fi
done

# A MISSING TOOL IS EXIT 2, NEVER A PASS. Checked before any file is read, so the failure
# names the tool rather than surfacing as an empty scan that looks clean. `docker compose
# version` is the one that matters: a host carrying only Compose v1 (`docker-compose`) has
# `docker` on PATH and cannot answer.
#
# NOT GATED ON A MAJOR VERSION, deliberately. The runner is on 2.38.2 and the next image
# carries 5.1.3, so a `v2`-shaped check would fail the build on an upgrade that may well be
# fine. The suite is where a behaviour change is decided; this only establishes that a
# subcommand exists to ask.
command -v docker >/dev/null 2>&1 || { echo "::error::compose-loopback-guard: docker not on PATH — cannot resolve the compose model." >&2; exit 2; }
command -v jq >/dev/null 2>&1 || { echo "::error::compose-loopback-guard: jq not on PATH." >&2; exit 2; }
compose_version=$(docker compose version --short 2>/dev/null) || {
  echo "::error::compose-loopback-guard: 'docker compose' unavailable (Compose v2 or newer required)." >&2
  exit 2
}

errfile=$(mktemp)
trap 'rm -f "$errfile"' EXIT

# `--no-interpolate` so the guard needs no `.env`: it must gate the project in CI, where no
# secret exists, and the repo's compose file makes three variables hard `:?` requirements.
# Verified 2026-08-04 that no `ports:` value in this repo is interpolated, so nothing under
# ports is lost. It also runs with the daemon DOWN — `config` is client-side, and every
# measurement behind this file was taken that way.
#
# THE FLAG CARRIES A SECOND REASON, AND IT IS A SECURITY ONE. Without it, compose resolves
# `.env` into the model: measured, `POSTGRES_PASSWORD` comes back as the literal password
# instead of `${POSTGRES_PASSWORD_DEV:?...}`. The guard never prints `$model`, so nothing
# leaks today — but the natural reason to reach for `--interpolate` is to "fix" the
# UNRESOLVED-ENTRY refusals, and that trade weakens the guard AND pulls real secrets into a
# variable one `echo` away from a CI log. Both reasons have to fall before the flag moves.
#
# ONE INVOCATION PER PROJECT. Merging is no longer the thing to avoid — WITHIN a project it
# is precisely what is wanted, because override semantics is what `docker compose up` applies
# and this guard's whole point is to judge what runs. What must never merge is two SEPARATE
# projects, so each argument gets its own invocation and "is each of these projects clean"
# stays the question being answered.
#
# Profile-gated services are included WITHOUT `--profile '*'` — measured on this repo's own
# file, whose `postgres-test` and `redis-test` sit behind the `test` profile and whose six
# ports are all counted. Their bindings were otherwise provable only by not running them.
violations=""
recognised=0

for d in "${dirs[@]}"; do
  if ! model=$(docker compose --project-directory "$d" config --no-interpolate --format json 2>"$errfile"); then
    echo "::error::compose-loopback-guard: compose refused the project in $d — the guard cannot answer." >&2
    cat "$errfile" >&2
    exit 2
  fi

  # AN ENTRY IS NOT ALWAYS A MAPPING, and the shapes that stay raw are not exotic. Measured:
  # an unexpanded `"127.0.0.1:${HOST_PORT}:5432"` under `--no-interpolate`, and a hostname
  # bind address `"localhost:9000:9000"`, both come back as the RAW STRING. The guard cannot
  # read a bind address compose did not resolve, so it refuses. Letting jq crash on it would
  # refuse too — with a message blaming the model instead of naming the entry.
  out=$(jq -r --arg p "$d" '
    def is_ipv4: (type == "string") and test("^[0-9]{1,3}(\\.[0-9]{1,3}){3}$");
    def is_loopback: (. == "::1") or (is_ipv4 and startswith("127."));
    def is_wide: (. == "::") or (is_ipv4 and (startswith("127.") | not));
    # A service reaches host networking through a top-level network whose RESOLVED name is
    # `host`, with no `network_mode` key anywhere in its model. Compose fills `name` in from
    # the key when it is omitted, so matching on the resolved name covers both spellings.
    # THE HOST-NETWORKING AXIS IS THREE LITERAL COMPARISONS, AND A VARIABLE DEFEATS ANY OF
    # THEM. Under `--no-interpolate` compose hands back the raw string, so `== "host"` cannot
    # see what the value becomes at `up` time. All three seats were measured exiting 0 while
    # `docker compose up` gave host networking:
    #   network_mode: "${NETMODE}"                       -> the mode the service declares
    #   networks: {n: {external: true, name: "${HOST}"}} -> the name a network resolves to
    #   networks: ["${NET}"]                             -> which network is joined
    # `"${NETMODE:-host}"` is the worst of them: it names `host` as its own DEFAULT, so the
    # file gives host networking even with no variable set, and the guard read it as clean.
    #
    # ONE PREDICATE FOR ALL THREE, deliberately. Repairing the seat that was reported and
    # leaving its siblings is how this class survived a round: the repair must be the class,
    # not the instance.
    #
    # THE REF SEAT IS UNCONDITIONAL, AND NARROWING IT REOPENS A MEASURED HOLE. The tempting
    # optimisation is to refuse an unresolved attachment only when the file also declares a
    # network that resolves to `host`. Measured, that misses this file:
    #
    #   networks: {n: {external: true, name: "${HOST}"}}
    #   services: {a: {networks: ["${NET}"], ports: ["127.0.0.1:1:1"]}}
    #
    # With HOST=host and NET=n it is host networking. The NAME seat cannot fire, because
    # `unresolved_nets` holds the key `n` while `attached` holds `${NET}` and the two never
    # intersect. A narrowed REF seat cannot fire either, because NO declared network resolves
    # to the literal `host` — the only candidate is itself a variable. Only the broad rule
    # sees it. So the breadth is load-bearing through the interaction of two seats, and the
    # consistency argument below is the weaker of the two grounds, not the reason.
    #
    # (It is also consistent with UNRESOLVED-ENTRY on the ports axis, which refuses
    # `localhost:9000:9000` though that binding is in fact loopback. Refusing what cannot be
    # read beats guessing at it. The over-refusal is real — `"${STACK}_backend"` never becomes
    # `host` and is refused anyway — but it is exit 2, "I could not read this", which is true.)
    def is_unresolved: (type == "string") and test("\\$");
    def host_nets: [ (.networks // {}) | to_entries[] | select(.value.name == "host") | .key ];
    def unresolved_nets: [ (.networks // {}) | to_entries[]
                           | select(.value.name | is_unresolved) | .key ];
    def attached: (.networks // {}) | if type == "object" then keys else . end;

    host_nets as $hostnets
    | unresolved_nets as $unresolved
    | (.services // {}) as $svc
    | [ $svc | to_entries[]
        | select(.value.network_mode == "host")
        | "\($p): HOST-NETWORKING service=\(.key) via=network_mode" ]
    + [ $svc | to_entries[] as $s
        | ($s.value | attached)[]
        | select(. as $n | $hostnets | index($n))
        | "\($p): HOST-NETWORKING service=\($s.key) via=network:\(.)" ]
    + [ $svc | to_entries[]
        | select(.value.network_mode | is_unresolved)
        | "\($p): UNRESOLVED-NETWORK-MODE service=\(.key) network_mode=\(.value.network_mode)" ]
    + [ $svc | to_entries[] as $s
        | ($s.value | attached)[] as $n
        | select($unresolved | index($n))
        | "\($p): UNRESOLVED-NETWORK-NAME service=\($s.key) network=\($n)" ]
    + [ $svc | to_entries[] as $s
        | ($s.value | attached)[] as $n
        | select($n | is_unresolved)
        | "\($p): UNRESOLVED-NETWORK-REF service=\($s.key) network=\($n)" ]
    + [ $svc | to_entries[] as $s
        | ($s.value.ports // [])[]
        | select(type != "object")
        | "\($p): UNRESOLVED-ENTRY service=\($s.key) entry=\(. | tostring)" ]
    + [ $svc | to_entries[] as $s
        | ($s.value.ports // [])[]
        | select(type == "object")
        | select(has("host_ip") and (((.host_ip | is_loopback) or (.host_ip | is_wide)) | not))
        | "\($p): UNJUDGED-BIND-IP service=\($s.key) published=\(.published // "<ephemeral>") host_ip=\(.host_ip)" ]
    + [ $svc | to_entries[] as $s
        | ($s.value.ports // [])[]
        | select(type == "object")
        | select((has("host_ip") | not) or (.host_ip | is_wide))
        | "\($p): NOT-LOOPBACK service=\($s.key) published=\(.published // "<ephemeral>") host_ip=\(.host_ip // "<absent>")" ]
    | .[]
  ' <<<"$model") || { echo "::error::compose-loopback-guard: could not read the resolved model for $d." >&2; exit 2; }

  # The same guard as the call above, and for the same reason: `jq` exits 5 on a runtime
  # error, which `set -e` would propagate straight out of the documented 0/1/2 contract.
  n=$(jq '[(.services // {})[] | (.ports // []) | length] | add // 0' <<<"$model") \
    || { echo "::error::compose-loopback-guard: could not count published ports for $d." >&2; exit 2; }
  recognised=$((recognised + n))

  # `+=` with an explicit newline, NOT `violations=$(printf ...)`: command substitution strips
  # the trailing newline, so the last finding of one file fused with the first of the next and
  # a single line named two files. The exit code never moved (`grep -qE` matches substrings,
  # so fusion can only add marker hits), but a guard whose whole thesis is that its message
  # names the service and port truthfully cannot print a line naming two files.
  if [ -n "$out" ]; then
    violations+="$out"$'\n'
  fi
done

if [ -n "$violations" ]; then
  # "could not answer" is exit 2 and must never collapse into exit 1. The markers below all
  # mean the guard did not READ the binding, not that it read it and it was wide.
  #
  # HERESTRING, NOT A PIPE. `grep -q` leaves at its first match; a producer still holding
  # more than a pipe buffer then dies of SIGPIPE, and `pipefail` makes that the pipeline's
  # status — so the `if` went false on exactly the inputs it exists to catch, and the guard
  # printed "not bound to loopback" about entries it had only failed to read. Measured on the
  # awk predecessor (#1206), and the reason that guard's Blocker was a Blocker. Do not
  # restore the pipe; `classifier_survives_large_refusal_list` fails if it comes back.
  # INVERTED, SO THE RULE IS THE MECHANISM AND NOT A LIST (#1216). The previous form
  # enumerated the refusal markers, which is the same shape that let a repair land on one seat
  # of three: a new marker had to be remembered in two places, and the second place is silent
  # when forgotten. A refusal class added later would have fallen through to exit 1 — the
  # guard announcing "not bound to loopback" about a state it never read, which is #1206's
  # Blocker form. Now only a READ bind address is a finding, and everything else is a refusal
  # by default, so a new marker needs no second edit.
  #
  # `|^$` IS LOAD-BEARING, NOT DEFENSIVE. `$violations` already ends in a newline and the
  # herestring appends its own, so the input carries a trailing BLANK line. Without `^$` the
  # `-v` match hits that blank line on every run and the guard exits 2 UNCONDITIONALLY —
  # including on `bare_mapping`, which is the one fixture that must stay exit 1 for this guard
  # to be worth running. Measured by `code-reviewer` on #1215 before the inversion was written.
  if grep -qvE ': NOT-LOOPBACK |^$' <<<"$violations"; then
    echo "::error::compose-loopback-guard: a published port is in a state this guard will not judge." >&2
    echo "HOST-NETWORKING publishes outside ports: entirely. UNRESOLVED-ENTRY is an entry compose" >&2
    echo "left as a raw string (an unexpanded variable, a hostname bind address)." >&2
    echo "UNRESOLVED-NETWORK-MODE/-NAME/-REF are the host-networking axis's three seats with an" >&2
    echo "unexpanded variable in them — the service's own network_mode, a network's resolved" >&2
    echo "name, or which network is joined. Any of the three can become 'host' at up time, so" >&2
    echo "write the value literally. (A name containing a literal, escaped \$\$ trips this too:" >&2
    echo "fail-closed, and Docker publishes no name pattern that says it is legal.) UNJUDGED-BIND-IP" >&2
    echo "is an IPv6 spelling other than ::1 or :: — it may well BE loopback, and saying otherwise" >&2
    echo "would assert a fact the guard has not established. All are refused, never passed." >&2
    echo "This list describes today's markers; the RULE is that anything which is not a bind" >&2
    echo "address the guard actually read lands here, so a marker not named above is one too." >&2
    printf '%s' "$violations" >&2
    exit 2
  fi
  echo "::error::compose-loopback-guard: published port(s) not bound to loopback" >&2
  # No trailing \n: `$violations` already ends in one, and printing a second produced a blank
  # line before the explanation.
  printf '%s' "$violations" >&2
  echo >&2
  echo "Every published port must name a loopback bind address (#1198)." >&2
  echo "host_ip=<absent> means compose read no bind address at all — it omits the key rather" >&2
  echo "than emitting 0.0.0.0, and the result binds every interface. Two shapes reach it, and" >&2
  echo "they do NOT have the same fix:" >&2
  echo "  a bare HOST:CONTAINER  (e.g. 5435:5432)  -> write 127.0.0.1:5435:5432" >&2
  echo "  the container port alone (5435, or a long form with no published:) -> that publishes" >&2
  echo "  on a RANDOM host port across every interface; write 127.0.0.1::5435 to keep the" >&2
  echo "  random port and bind it to loopback. Those are reported as published=<ephemeral>." >&2
  echo "In the LONG form neither remedy needs a syntax change: add host_ip: 127.0.0.1 to the" >&2
  echo "entry and leave target:/published: alone." >&2
  exit 1
fi

if [ -z "$expect_min" ]; then
  # No floor given: zero recognised entries is refused rather than reported as clean.
  # "I found no ports, therefore all ports are fine" is vacuous truth wearing a clean bill of
  # health.
  if [ "$recognised" -eq 0 ]; then
    echo "::error::compose-loopback-guard: recognised ZERO published ports." >&2
    echo "Refused rather than reported clean. Pass --expect-min 0 if zero really is expected." >&2
    exit 2
  fi
elif [ "$recognised" -eq 0 ] && [ "$expect_min" -gt 0 ]; then
  # Refused here for the same reason as the no-floor branch. `--expect-min 0` is exempt on
  # purpose — it is the one way to STATE that zero is expected, and collapsing it into a
  # refusal would leave no way to say so at all.
  echo "::error::compose-loopback-guard: recognised ZERO published ports (floor was $expect_min)." >&2
  echo "Pass --expect-min 0 if zero really is expected here." >&2
  exit 2
elif [ "$recognised" -lt "$expect_min" ]; then
  echo "::error::compose-loopback-guard: expected at least $expect_min published port(s), recognised $recognised" >&2
  echo "Passing with fewer than expected means the ports moved or were deleted — not a clean bill." >&2
  exit 1
fi

echo "compose-loopback-guard: OK — $recognised published port(s), all loopback-bound (${#dirs[@]} project(s), Compose $compose_version)"
