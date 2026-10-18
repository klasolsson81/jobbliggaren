"use client";

// "use client": it calls an effect hook that moves DOM focus — browser-only.

import { useFocusMainOnChange } from "@/lib/hooks/use-focus-main-on-change";

// design-reviewer Blocker 1, PR #2180 (#2172).
export function PublicCataloguePageFocus({ page }: { page: number }) {
  useFocusMainOnChange(page);
  return null;
}
