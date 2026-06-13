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

# Frontend: `pnpm build` is a mandatory pre-push gate for RSC/client-boundary changes

When a change touches the RSC↔Client boundary (props passed from a Server
Component into a `"use client"` island, slot/children composition, server-
rendered nodes handed to client components), `pnpm build` must be run and be
green before push. `pnpm build` runs the production RSC payload generation —
it is the only mechanism that catches serialization and RSC-runtime errors.
vitest, tsc and eslint cannot: jsdom isolates the component from the RSC
boundary, so a non-serializable prop (e.g. a function passed to a client
component) passes unit tests but fails at server render in production.
