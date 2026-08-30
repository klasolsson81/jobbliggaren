#!/usr/bin/env bash
# rclone-invocation-guard.test.sh — fixtures for the #1289 guard.
#
# THE LAST THREE CASES PIN THE DELIVERY ITSELF, not a fixture of it: the real repo root, once by
# argument and once by the default, plus the mutation case senior-cto-advisor required — a copy
# of the REAL `jobbliggaren-logship.sh` with one `rclone serve` line added, which must go red. A
# guard that has never been observed failing on the actual delivered file is not coverage; it is
# a green light with no wiring behind it.
#
# Every case that asserts 0 is as load-bearing as every case that asserts 1. This guard reads
# shell source, and the honest text under `deploy/` genuinely contains the word `rclone` in
# prose, in variable names and in a `for tool in …` list. A guard that fires on any of those gets
# switched off, and a switched-off guard is worth less than none — case 3 below is the one that
# actually caught a false positive during development (backup.test.sh:504).
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

# Build a throwaway repo carrying one script under deploy/systemd/. The guard reads the INDEX
# (`git ls-files`), so `stage` is not optional bookkeeping — an unstaged file is invisible to it.
mkproj() {
  local d="$TMPROOT/$1"
  rm -rf "$d"
  mkdir -p "$d/deploy/systemd"
  git -C "$d" init -q
  git -C "$d" config user.email t@example.invalid
  git -C "$d" config user.name t
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
gzip -c "$f" | age -R "$rcpt" | rclone rcat "${RCLONE_FLAGS[@]}" "$object"
remote_sha=$(rclone cat "${RCLONE_FLAGS[@]}" "${BACKUP_REMOTE}/${DEK}" | sha256sum)
EOF
stage "$d"
run_case "delivered rcat + cat" 0 "$d"

d=$(mkproj green-prose)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
# The rclone config is a credential, so it is materialised on the SAME tmpfs.
# It wants the base64 of a complete rclone config file, not the config itself.
for tool in docker age rclone sha256sum flock; do
  command -v "$tool" >/dev/null || die "missing $tool"
done
rclone_config="${WORKDIR}/rclone.conf"
die "shipping failed (gzip=${rc[0]} age=${rc[1]} rclone=${rc[2]})."
rclone rcat "${RCLONE_FLAGS[@]}" "$object"
EOF
stage "$d"
run_case "honest prose, for-list, var names" 0 "$d"

# The case that actually caught a false positive while this guard was being written: an INLINE
# comment whose parenthesised text parses as a command segment naming the verb "stub".
d=$(mkproj green-inline-comment)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
build_sut_copy   # (rclone stub is restored below)
rclone rcat "${RCLONE_FLAGS[@]}" "$object"
EOF
stage "$d"
run_case "inline comment naming rclone is not a call" 0 "$d"

# A global flag BEFORE the subcommand is refused, and this case pins that the refusal is a
# deliberate parse boundary rather than an accident. The alternative — skipping flags — requires
# knowing which of rclone's several hundred flags take a separate value, and a flag whose value
# was then read as the subcommand produced the genuinely confusing message
# "subcommand '\"$c\"' is not in the allowlist" during development. Refusing the shape outright
# says what is wrong. All six delivered invocations already put the verb first.
d=$(mkproj red-flag-before-verb)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
rclone --config "$c" --retries 3 rcat "$object"
EOF
stage "$d"
run_case "global flag before the subcommand is refused" 1 "$d"

# --------------------------------------------------------------------------------------------
# RED — one case per forbidden subcommand, each naming the CVE class it opens
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

# The blind spot a single command-position regex has: `rclone` after a shell KEYWORD rather than
# after `|`, `;` or start-of-line. Rule A's keyword stripping is what sees this; rule B is the
# net under it. Both must hold, so this case is not redundant with the loop above.
d=$(mkproj red-keyword-position)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
if [ -n "$x" ]; then rclone rcd --rc-serve --rc-addr :5572; fi
EOF
stage "$d"
run_case "forbidden verb after 'then' (keyword position)" 1 "$d"

d=$(mkproj red-do-position)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
for o in a b; do rclone sync "$src" "$o"; done
EOF
stage "$d"
run_case "forbidden verb after 'do' (keyword position)" 1 "$d"

d=$(mkproj red-pipe-position)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
cat "$f" | rclone serve s3 --addr :8080
EOF
stage "$d"
run_case "forbidden verb after a pipe" 1 "$d"

# Rule B keeps the RAW line, so a forbidden verb cannot hide behind a `#`. This is the half of
# the inline-comment split that stays fail-closed, and it is why case "green-inline-comment"
# above does not open a hole.
d=$(mkproj red-verb-in-inline-comment)
cat >"$d/deploy/systemd/x.sh" <<'EOF'
#!/usr/bin/env bash
true   # someday we might rclone serve s3 here
rclone rcat "$object"
EOF
stage "$d"
run_case "forbidden verb inside an inline comment still fails (rule B)" 1 "$d"

# --------------------------------------------------------------------------------------------
# RED — forbidden flags, and the unparseable forms that must fail closed
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
rclone lsjson "$remote"
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

# An empty scope is the failure mode a DYNAMIC scope brings with it, and a guard that passes
# vacuously when it can see nothing is the defect this whole file exists to prevent.
d=$(mkproj red-empty-scope)
echo "placeholder" >"$d/README.md"
stage "$d"
run_case "empty scope fails closed" 1 "$d"

# --------------------------------------------------------------------------------------------
# THE MUTATION CASE — required by senior-cto-advisor, and the only one that touches real source
# --------------------------------------------------------------------------------------------
#
# A copy of the REAL logship script, unmodified, must pass; the SAME copy with one `rclone serve`
# line added must fail. Asserting both arms is the point: a guard that fails on the mutated copy
# but also on the clean one has measured nothing.
d=$(mkproj mutation-logship)
cp "$REPO_ROOT/deploy/systemd/jobbliggaren-logship.sh" "$d/deploy/systemd/"
stage "$d"
run_case "real logship.sh, unmutated" 0 "$d"

# Assert the mutation actually landed before running the guard on it. A mutation that did not
# apply gives a green run that measures nothing.
mut="$d/deploy/systemd/jobbliggaren-logship.sh"
printf '\nrclone serve s3 --addr :8080 "$REMOTE_PREFIX"\n' >>"$mut"
landed=$(grep -c 'rclone serve s3' "$mut")
if [ "$landed" != "1" ]; then
  echo "FAIL [mutation harness]: expected the mutation to land exactly once, counted $landed"
  fails=$((fails + 1))
fi
stage "$d"
run_case "real logship.sh + one 'rclone serve' line (MUTANT)" 1 "$d"

# --------------------------------------------------------------------------------------------
# THE DELIVERY ITSELF — both invocation forms. A workflow step passing no argument must judge
# the same thing the explicit form does, or the step and this suite disagree silently.
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
