"use client";

import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";

// The code can no longer be used: the backend answered 410 for this challenge. The panel replaces
// the FIELD, not the form, so the step is intact and its h1 still describes the page; a heading
// here would only say the body twice (design-reviewer, #1738). "Skicka ny kod" below becomes the
// primary.
//
// `expired` names no cause, because the page cannot know one: the backend answers the same for a
// code that ran out, was used, was replaced by a newer one, or never had a record. `burned` does
// name its cause, and names the way on that still works: the burn is the code arm's only, and the
// link in the same mail still logs in.
export function DeadCodePanel({ reason }: { reason: "expired" | "burned" }) {
  const t = useTranslations("pages");
  const panelRef = useFocusOnMount<HTMLDivElement>();

  return (
    <div ref={panelRef} tabIndex={-1} role="status" aria-live="polite">
      <p className="text-body text-text-primary">{t(`auth.passwordless.code.${reason}`)}</p>
    </div>
  );
}
