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

Since #1053 `pnpm build` also runs in the **blocking** `frontend` CI job, so a
broken production build now fails `ci` instead of reporting success.

**A green production build does NOT mean `next dev` compiles.** Both CI jobs
that build, build for production; webpack defers `"use server"` export checks
to runtime while Turbopack rejects at module-link time — which is how #1059
lived three weeks on `main` with every build green.

`pnpm lint` enforces **one part** of Next's E352 rule — the part that broke:
a `"use server"` module may not use *specifier* exports (`export { x }`,
`export type { X }`, `export { type X }`, `export * from …` and the
`export default`-specifier variants). That is the form a type-only binding can
leave a module by, so it is the form TypeScript's erasure turns into a
re-export pointing at nothing. Write `export async function foo() {}` directly,
and put shared types in a module without the directive —
`src/lib/actions/_action-result.ts` is the SSOT.

It does **not** check that every export is async: `export const x = 1`,
a synchronous `export function`, and `export default function` all pass lint
today, and `export const x = 1` is caught by no gate at all (Next's
`action-validate.js` states in its own header that it checks *during the
runtime*). Do not read a green lint as "this module satisfies E352".
