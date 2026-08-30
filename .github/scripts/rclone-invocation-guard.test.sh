#!/usr/bin/env bash
# rclone-invocation-guard.test.sh — fixtures for the #1289 guard.
#
# THE FINAL CASES PIN THE DELIVERY ITSELF, not a fixture of it: the real repo root, once by
# argument and once by the default, plus two mutation pairs — copies of the REAL
# `jobbliggaren-logship.sh` with one hostile line added, which must go red while the clean copy
# stays green. A guard that has never been observed failing on the actual delivered file is not
# coverage; it is a green light with no wiring behind it.
#
# THE EIGHT BYPASS CASES ARE THE HEART OF THIS SUITE. The first version of the guard was a single
# command-position parser, and it passed all of its own fixtures. `security-auditor` and
# `dotnet-architect` each built adversarial fixtures against it on 2026-08-30 and between them
# measured eight idiomatic shell forms that ran a forbidden subcommand — `rcd`, `copyto` or
# `lsjson` — while the guard stayed GREEN. Every one of those is a case below. They are not
# hypothetical shapes: they are the measured ways this guard has already been defeated, and the
# reason it now has four rules.
#
# Every case that asserts 0 is as load-bearing as every case that asserts 1. This guard reads
# shell source, and honest text under `deploy/` genuinely contains `rclone` in prose, in variable
# names, in a `for tool in …` list and inside a `die` message. Three of the green cases below are
# false positives this suite actually caught during development.
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/rclone-invocation-guard.sh"
REPO_ROOT=$(cd -- "$script_dir/../.." && pwd)
readonly REPO_ROOT

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

fails=0
cases=0

# The guard enforces a FLOOR on its dynamic scope (MIN_SCOPED), so a one-file fixture would fail
# on the floor rather than on the predicate under test and every case would pass for the wrong
# reason. Padding to the floor is deliberate: it exercises the DELIVERED configuration instead of
# making the floor overridable, which would put a bypass in the shipped guard to serve its tests.
# `scope_floor` reads the floor out of the guard itself, so raising it there cannot silently
# strand this suite.
scope_floor() { sed -n 's/^readonly MIN_SCOPED=\([0-9]*\).*/\1/p' "$SUT"; }
FLOOR=$(scope_floor)
readonly FLOOR
if [ -z "$FLOOR" ]; then
  echo "FAIL [harness]: could not read MIN_SCOPED out of the guard — the suite would pad blind"
  exit 1
fi

# Build a throwaway repo carrying one script under test plus enough inert padding to clear the
# floor. The guard reads the INDEX (`git ls-files`), so `git add` is not bookkeeping — an
# unstaged file is invisible to it.
mkproj() {
  local d="$TMPROOT/$1"
  rm -rf "$d"
  mkdir -p "$d/deploy/systemd"
  git -C "$d" init -q
  git -C "$d" config user.email t@example.invalid
  git -C "$d" config user.name t
  local i
  for i in $(seq 2 "$FLOOR"); do
    printf '#!/usr/bin/env bash\ntrue\n' >"$d/deploy/systemd/pad$i.sh"
  done
  printf '%s' "$d"
}

stage() { git -C "$1" add -A; }

# run_case <name> <expected-exit> <project-dir>
run_case() {
  local name="$1" want="$2" dir="$3" got=0
  cases=$((cases + 1))
  set +e
  bash "$SUT" "$dir" >"$TMPROOT/out.$cases" 2>&1
  got=$?
  set -e
  if [ "$got" != "$want" ]; then
    fails=$((fails + 1))
    echo "FAIL [$name]: expected exit $want, got $got"
    sed 's/^/      | /' "$TMPROOT/out.$cases"
  else
    echo "ok   [$name] (exit $got)"
  fi
}

# --------------------------------------------------------------------------------------------
# GREEN — the delivered shapes, and the honest text that must not fire
# --------------------------------------------------------------------------------------------

d=$(mkproj green-delivered)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
readonly RCLONE_FLAGS=(--config "$rclone_config" --log-level NOTICE --retries 3)
rclone_config="${WORKDIR}/rclone.conf"
gzip -c "$f" | age -R "$rcpt" | rclone rcat "${RCLONE_FLAGS[@]}" "$object"
remote_sha=$(rclone cat "${RCLONE_FLAGS[@]}" "${BACKUP_REMOTE}/${DEK}" | sha256sum)
EOF
stage "$d"
run_case "delivered rcat + cat, with rclone_config nearby" 0 "$d"

d=$(mkproj green-prose)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
# The rclone config is a credential, so it is materialised on the SAME tmpfs.
# It wants the base64 of a complete rclone config file, not the config itself.
for tool in docker age rclone sha256sum flock; do
  command -v "$tool" >/dev/null || die "missing $tool"
done
die "shipping failed (gzip=${rc[0]} age=${rc[1]} rclone=${rc[2]})."
rclone rcat "${RCLONE_FLAGS[@]}" "$object"
EOF
stage "$d"
run_case "honest prose, for-list, rc[] array" 0 "$d"

# FALSE POSITIVE CAUGHT DURING DEVELOPMENT (1/3): an inline comment whose parenthesised text
# parsed as a command segment naming the verb "stub".
d=$(mkproj green-inline-comment)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
build_sut_copy   # (rclone stub is restored below)
rclone rcat "${RCLONE_FLAGS[@]}" "$object"
EOF
stage "$d"
run_case "inline comment naming rclone is not a call" 0 "$d"

# FALSE POSITIVE CAUGHT DURING DEVELOPMENT (2/3): rule D's closing character class briefly
# admitted `)`, which fired on backup.sh:292's real die message.
d=$(mkproj green-die-message)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
die "backup failed at stage (1=pg_dump 2=age 3=rclone). No stamp is written."
rclone rcat "$object"
EOF
stage "$d"
run_case "die message listing rclone as a stage is not a binding" 0 "$d"

# FALSE POSITIVE CAUGHT DURING DEVELOPMENT (3/3): `config` in the blocklist fired on the real
# inject-secrets die message. It is excluded from rule B for exactly this, and rule A still
# rejects it in command position — which the red case below pins.
d=$(mkproj green-config-prose)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
die "${key} is not valid base64. It is the base64 OF an rclone config file, not the config itself"
rclone rcat "$object"
EOF
stage "$d"
run_case "rclone config as prose in a die message" 0 "$d"

d=$(mkproj green-path-literal)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
/usr/bin/rclone rcat "$object"
EOF
stage "$d"
run_case "path-qualified binary with an allowed verb" 0 "$d"

# --------------------------------------------------------------------------------------------
# RED — the EIGHT measured bypasses. Each of these passed GREEN against the first version of the
# guard while invoking a subcommand that opens a CVE class.
# --------------------------------------------------------------------------------------------

d=$(mkproj red-bypass-defaulted-var)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
RCLONE="${RCLONE:-rclone}"
"$RCLONE" rcd --rc-addr :5572 --rc-no-auth
EOF
stage "$d"
run_case "BYPASS 1: defaulted variable binding, then rcd" 1 "$d"

d=$(mkproj red-bypass-plain-var)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
RCLONE=rclone
"$RCLONE" rcd
EOF
stage "$d"
run_case "BYPASS 2: plain variable binding, then rcd" 1 "$d"

d=$(mkproj red-bypass-var-unquoted)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
RCLONE_BIN=rclone
$RCLONE_BIN serve restic /data
EOF
stage "$d"
run_case "BYPASS 3: unquoted variable expansion, then serve" 1 "$d"

d=$(mkproj red-bypass-path-in-var)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
RC=/usr/bin/rclone; "$RC" rcd --rc-addr :5572 --rc-no-auth
EOF
stage "$d"
run_case "BYPASS 4: path bound to a variable, semicolon-terminated" 1 "$d"

d=$(mkproj red-bypass-hash-in-string)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
echo "a # b" ; rclone lsjson "$remote"
EOF
stage "$d"
run_case "BYPASS 5: hash inside a string truncates rule A's view" 1 "$d"

d=$(mkproj red-bypass-varprefix)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
RCLONE_CONFIG=x rclone lsjson "$remote"
EOF
stage "$d"
run_case "BYPASS 6: VAR=value prefix before the binary" 1 "$d"

d=$(mkproj red-bypass-wrapper)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
timeout 30 rclone copyto "$rem" /etc/x
EOF
stage "$d"
run_case "BYPASS 7: wrapper command, and copyto writes locally" 1 "$d"

d=$(mkproj red-bypass-xargs)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
echo x | xargs rclone lsjson
EOF
stage "$d"
run_case "BYPASS 8: xargs as the command word" 1 "$d"

# --------------------------------------------------------------------------------------------
# RED — one case per originally-blocklisted subcommand, plus the keyword positions
# --------------------------------------------------------------------------------------------

for verb in rcd serve mount sync copy archive; do
  d=$(mkproj "red-verb-$verb")
  {
    echo '#!/usr/bin/env bash'
    echo "rclone $verb \"\$target\""
  } >"$d/deploy/systemd/x.sh"
  stage "$d"
  run_case "forbidden verb: $verb" 1 "$d"
done

d=$(mkproj red-keyword-then)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
if [ -n "$x" ]; then rclone rcd --rc-serve --rc-addr :5572; fi
EOF
stage "$d"
run_case "forbidden verb after 'then'" 1 "$d"

d=$(mkproj red-keyword-do)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
for o in a b; do rclone sync "$src" "$o"; done
EOF
stage "$d"
run_case "forbidden verb after 'do'" 1 "$d"

# Rule B keeps the RAW line, so a forbidden verb cannot hide behind a `#`. This is the half of
# the inline-comment split that stays fail-closed, and it is why the green inline-comment case
# above does not open a hole.
d=$(mkproj red-verb-in-inline-comment)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
true   # someday we might rclone serve s3 here
rclone rcat "$object"
EOF
stage "$d"
run_case "forbidden verb inside an inline comment (rule B)" 1 "$d"

# `config` is out of rule B's blocklist so it does not fire on prose. Rule A catches it in plain
# command position, which this pins — but ONLY there: the guard's own constant block records the
# forms that get past it, and this case must not be read as covering them.
d=$(mkproj red-config-invoked)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
rclone config create remote s3
EOF
stage "$d"
run_case "rclone config in plain command position fails (rule A)" 1 "$d"

# --------------------------------------------------------------------------------------------
# RED — flags, unparseable forms, and the two scope failures
# --------------------------------------------------------------------------------------------

for flag in --links --metadata; do
  d=$(mkproj "red-flag-${flag#--}")
  {
    echo '#!/usr/bin/env bash'
    echo "rclone rcat $flag \"\$object\""
  } >"$d/deploy/systemd/x.sh"
  stage "$d"
  run_case "forbidden flag: $flag" 1 "$d"
done

d=$(mkproj red-unknown-verb)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
rclone frobnicate "$remote"
EOF
stage "$d"
run_case "verb outside the allowlist fails closed" 1 "$d"

d=$(mkproj red-no-verb)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
rclone --config "$c"
EOF
stage "$d"
run_case "invocation with no verb fails closed" 1 "$d"

# A global flag BEFORE the subcommand is refused as its own error rather than parsed. Skipping
# flags would mean knowing which of rclone's several hundred take a value, and a flag whose value
# was read as the subcommand produced a genuinely confusing message during development.
d=$(mkproj red-flag-before-verb)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
rclone --config "$c" --retries 3 rcat "$object"
EOF
stage "$d"
run_case "global flag before the subcommand is refused" 1 "$d"

# The two failure modes a DYNAMIC scope brings with it. A guard that passes vacuously when it can
# see nothing — or can see only some of what it should — is the defect this file exists to stop.
d=$(mkproj red-empty-scope)
rm -f "$d"/deploy/systemd/*.sh
echo "placeholder" >"$d/README.md"
stage "$d"
run_case "empty scope fails closed" 1 "$d"

d=$(mkproj red-narrowed-scope)
rm -f "$d/deploy/systemd/pad2.sh"
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
rclone rcat "$object"
EOF
stage "$d"
run_case "scope below the floor fails closed even when the code is clean" 1 "$d"

# --------------------------------------------------------------------------------------------
# MUTATION — the only cases that touch real source. Both arms are asserted: a guard that fails on
# the mutant but also on the clean copy has measured nothing.
# --------------------------------------------------------------------------------------------

d=$(mkproj mutation-logship)
cp "$REPO_ROOT/deploy/systemd/jobbliggaren-logship.sh" "$d/deploy/systemd/"
stage "$d"
run_case "real logship.sh, unmutated" 0 "$d"

mut="$d/deploy/systemd/jobbliggaren-logship.sh"
printf '\nrclone serve s3 --addr :8080 "$REMOTE_PREFIX"\n' >>"$mut"
landed=$(grep -c 'rclone serve s3' "$mut")
if [ "$landed" != "1" ]; then
  echo "FAIL [mutation harness A]: expected the mutation to land exactly once, counted $landed"
  fails=$((fails + 1))
fi
stage "$d"
run_case "real logship.sh + 'rclone serve' (MUTANT A)" 1 "$d"

# A second mutation in the shape the first guard could not see, against the same real file. This
# one is the regression test for the whole four-rule rewrite: it passed GREEN before it.
d=$(mkproj mutation-logship-indirect)
cp "$REPO_ROOT/deploy/systemd/jobbliggaren-logship.sh" "$d/deploy/systemd/"
mut="$d/deploy/systemd/jobbliggaren-logship.sh"
printf '\nRCLONE="${RCLONE:-rclone}"\n"$RCLONE" rcd --rc-addr :5572 --rc-no-auth\n' >>"$mut"
landed=$(grep -c 'rc-no-auth' "$mut")
if [ "$landed" != "1" ]; then
  echo "FAIL [mutation harness B]: expected the mutation to land exactly once, counted $landed"
  fails=$((fails + 1))
fi
stage "$d"
run_case "real logship.sh + variable-bound rcd (MUTANT B)" 1 "$d"

# --------------------------------------------------------------------------------------------
# THE DELIVERY ITSELF — both invocation forms. A workflow step passing no argument must judge the
# same thing the explicit form does, or the step and this suite disagree silently.
# --------------------------------------------------------------------------------------------

cases=$((cases + 1))
if bash "$SUT" "$REPO_ROOT" >"$TMPROOT/out.real" 2>&1; then
  echo "ok   [real repo, explicit root] (exit 0)"
else
  fails=$((fails + 1))
  echo "FAIL [real repo, explicit root]: the delivered tree does not satisfy its own guard"
  sed 's/^/      | /' "$TMPROOT/out.real"
fi

cases=$((cases + 1))
if ( cd "$REPO_ROOT" && bash "$SUT" ) >"$TMPROOT/out.default" 2>&1; then
  echo "ok   [real repo, default root] (exit 0)"
else
  fails=$((fails + 1))
  echo "FAIL [real repo, default root]: the no-argument form disagrees with the explicit one"
  sed 's/^/      | /' "$TMPROOT/out.default"
fi

echo
if [ "$fails" -ne 0 ]; then
  echo "$fails of $cases cases FAILED"
  exit 1
fi
echo "all $cases cases passed"
