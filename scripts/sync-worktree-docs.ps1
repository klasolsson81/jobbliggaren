#requires -Version 5
<#
.SYNOPSIS
    Sync gitignored local docs (listed in .worktreeinclude) from this main
    checkout into a freshly-created git worktree, so parallel Claude Code
    sessions share session-state docs (current-work.md, steg-tracker.md,
    tech-debt.md, sessions/, local reviews/ and ADRs 0074+).

.DESCRIPTION
    A fresh worktree branched from origin/main has the TRACKED files only;
    gitignored session-state docs (ADR 0072 docs-privacy) are absent. This
    script copies the .worktreeinclude entries into the target worktree,
    preserving relative paths.

    SAFETY: refuses to run onto the main checkout itself, and refuses any
    .worktreeinclude entry that looks like a secret (appsettings.Local,
    .env.local, *.pem, *.tfstate). Only the stack-owner main checkout runs
    against real secrets — see CLAUDE.md §6.5.

.PARAMETER WorktreePath
    Path to the target worktree (must already exist; see `git worktree list`).

.EXAMPLE
    scripts/sync-worktree-docs.ps1 C:\tmp\jobbliggaren-matching
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$WorktreePath
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$includeFile = Join-Path $repoRoot '.worktreeinclude'
if (-not (Test-Path $includeFile)) { throw ".worktreeinclude not found at $includeFile" }
if (-not (Test-Path $WorktreePath)) { throw "Worktree path not found: $WorktreePath" }

$dest = (Resolve-Path $WorktreePath).Path
if ($dest -eq $repoRoot) { throw "Refusing to sync onto the main checkout itself ($dest)." }

# Secret-file shapes: paths/names that ARE real secrets. Used in two places:
#  (1) reject an explicit .worktreeinclude entry (fail-closed throw — listing a
#      secret is a config error worth aborting on), and
#  (2) skip any secret-shaped file a DIRECTORY entry would otherwise recurse into
#      (skip-with-warning, so one stray file never aborts a legit dir sync).
# Deliberately PRECISE (extensions / known names), NOT broad words like "secret",
# so a legitimate doc such as "...-secret-sweep.md" still syncs.
$secretPatterns = @('appsettings.local', '.env.local', '.pem', '.pfx', '.key', '.tfstate', 'id_rsa')

function Test-SecretLike([string] $path) {
    $p = $path.ToLowerInvariant()
    foreach ($m in $secretPatterns) { if ($p.Contains($m)) { return $true } }
    return $false
}

$lines = Get-Content $includeFile |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') }

$copied = 0
$skipped = 0
foreach ($line in $lines) {
    if (Test-SecretLike $line) {
        throw "Refusing to sync secret-like entry from .worktreeinclude: '$line'"
    }
    $srcGlob = Join-Path $repoRoot $line
    $items = Get-ChildItem -Path $srcGlob -Recurse -File -Force -ErrorAction SilentlyContinue
    foreach ($item in $items) {
        $rel = $item.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
        if (Test-SecretLike $rel) {
            Write-Warning "Skipping secret-shaped file (NOT synced): $rel"
            $skipped++
            continue
        }
        $target = Join-Path $dest $rel
        $targetDir = Split-Path $target -Parent
        if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }
        Copy-Item -Path $item.FullName -Destination $target -Force
        $copied++
    }
}

Write-Output "Synced $copied local doc file(s) from main checkout into $dest (skipped $skipped secret-shaped file(s))"
