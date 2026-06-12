---
session: CC-struktur cold review — token-effektivisering
datum: 2026-06-12
slug: cc-struktur-cold-review
status: Batch A levererad (PR #55) · Batch B levererad (denna PR) · Batch C pending Klas approve-spec-edit
commits: se PR #55 + Batch B-PR
---

# Session 2026-06-12 — CC structure cold review (token efficiency)

> Written in English per this session's language decision (new docs in English;
> UI copy and Klas dialogue remain Swedish).

## Goal

Klas requested an objective cold review of the Claude Code setup with five
questions: (1) VS Code extension vs terminal, (2) is the 12-section start
prompt necessary, (3) compare against "The Froject", (4) model tiers per agent,
(5) English vs Swedish for docs. Target: token efficiency without quality loss.
Plan approved by Klas (plan mode + AskUserQuestion: Haiku direct, delete
froject-setup, run all three batches).

## Findings (web-verified 2026-06-12 against code.claude.com)

- **VS Code extension is Anthropic's recommended way** to run CC in VS Code;
  CLI-only features are marginal (full command set, `!` shortcut, tab
  completion). Token cost identical. → Stay in the extension.
- **Subagent `model:` supports aliases** (`opus`/`sonnet`/`haiku`/`fable`);
  omitted = `inherit` (main-loop model — the most expensive default). On-disk
  reality: **0/13 agents had a model field** (the previously reported
  ai-prompt-engineer hit was a prompt-template example in the body).
- **Every custom subagent loads CLAUDE.md + git status at startup** — CLAUDE.md
  size is a multiplier (main loop + every dispatch). JobbPilot's CLAUDE.md
  ≈ 8.8k tokens vs official "keep it concise" guidance (community ≤2k).
- **The Froject targets go-to-market teams**, not engineering; nothing JobbPilot
  lacks. One principle adopted: their agents are 1–2 KB vs JobbPilot's 14.5 KB
  average — agent bodies are per-dispatch system prompts.
- **Swedish costs more tokens than English** (English-trained BPE tokenizers);
  exact Claude figure unverified, estimated 20–40 % premium.
- Corrections of subagent inventory errors: the 368 MB project directory is
  on-disk transcript archive (NOT loaded into context); `claude-fable-5[1m]`
  in user settings is the intentional 1M-context variant.

## Delivered

**Batch A (PR #55, automerge):**
- ADR 0002 Amendment 2026-06-12: tier aliases in agent frontmatter (supersedes
  explicit-ID rule for `.claude/agents/` only; runtime config keeps pinned IDs
  per Amendment 2026-06-07).
- `model:` field on all 13 agents: 8× opus (decision/veto/code production),
  3× sonnet (db-migration-writer, adr-keeper, perf-test-writer), 2× haiku
  (test-runner, docs-keeper). Subagent model never affects the main loop
  (isolated context — no "switch back" exists).
- `.claude/README.md` reconciled (11→13 agents, tier column).
- Memory `project_agent_model_field_drift` closed.

**Batch B (this PR):**
- `docs/runbooks/session-start-template.md` rewritten 12→4 sections in English
  (~2–3k tokens saved per session start; removed duplication of auto-loaded
  context: CLAUDE.md §1.5/§9.2, MEMORY.md, ADR 0065).
- Five largest agent files trimmed 16–21 KB → 5.5–6.6 KB each
  (security-auditor, nextjs-ui-engineer, ai-prompt-engineer, code-reviewer,
  design-reviewer). Kept: role, criteria, veto rules, report format, tool
  access. Dropped: long examples, stale API advice, CLAUDE.md duplication.
- `docs/current-work.md` trimmed: E2i–D2 blocks moved to
  `current-work-archive.md`; stale status (E2j "pending merge") corrected to
  MERGAD #54; commit table completed with #50–#54.
- `C:\DOTNET-UTB\froject-setup` deleted (Klas GO; outside repo).

## Decisions

- Language policy: new ADRs, session logs, reviews, agent/skill files, commit
  messages in **English**; UI copy (`messages/sv.json`) and Klas dialogue stay
  **Swedish**; no mass translation of existing docs.
- ADR amendment authored by CC directly on explicit Klas GO (approved plan),
  per the Amendment 2026-06-07 precedent — adr-keeper not invoked (not in
  §9.2's mandatory list; precedent documented in the amendment's lifecycle
  note).
- Work isolated in a git worktree (`c:/tmp/cc-token-struktur`) — the main
  working tree held unparsed E2j leftovers.

## Next session / pending

- **Batch C (requires Klas `approve-spec-edit.sh`):** CLAUDE.md prune to
  ≤3.5k tokens in English; TD-lifecycle skill; session-protocol runbook;
  count_tokens verification.
- Verify in a fresh session that test-runner/docs-keeper dispatch on Haiku.
- Quality watch: next feature batch runs normal agent gates — rollback for any
  perceived regression is a one-line frontmatter edit.
