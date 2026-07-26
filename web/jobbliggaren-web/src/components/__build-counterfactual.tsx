// TEMPORARY — #1053 AC 2 counterfactual. Reverted in the next commit.
// A CLIENT component importing `next/headers` (server-only). This type-checks
// cleanly — the types exist — so `tsc --noEmit` passes; only `next build`
// rejects it. That is the point: the gate must catch what tsc cannot.
"use client";

import { cookies } from "next/headers";

export function BuildCounterfactual() {
  return <span data-c={String(!!cookies)} />;
}
