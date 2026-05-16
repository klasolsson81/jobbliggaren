<!-- BEGIN:nextjs-agent-rules -->

# Next.js: ALWAYS read docs before coding

Before any Next.js work, find and read the relevant doc in `node_modules/next/dist/docs/`. Your training data is outdated — the docs are the source of truth.

<!-- END:nextjs-agent-rules -->

# Frontend: visual verification is mandatory

When creating a new page or markedly changing rendered UI, run the
visual-verification loop (`pnpm visual-verify`) before reporting — see
[`docs/runbooks/frontend-visual-verification.md`](../../docs/runbooks/frontend-visual-verification.md).
Code review ≠ rendered-UI review. design-reviewer reviews the screenshots,
Klas approves them.
