"use client";

import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";

// Client because it takes focus when it mounts (`useFocusOnMount`).
//
// The code can no longer be used: the backend answered 410 for this challenge. The panel replaces
// the FIELD, not the form, so the step is intact and its h1 still describes the page; a heading
// here would only say the body twice (design-reviewer, #1738). "Skicka ny kod" below becomes the
// primary.
export function DeadCodePanel({
  reason,
  linksProvider = false,
}: {
  reason: "expired" | "burned";
  linksProvider?: boolean;
}) {
  const t = useTranslations("pages");
  const panelRef = useFocusOnMount<HTMLDivElement>();

  return (
    <div ref={panelRef} tabIndex={-1} role="status" aria-live="polite">
      <p className="text-body text-text-primary">
        {reason === "burned" && linksProvider
          ? t("auth.passwordless.code.burnedProvider")
          : t(`auth.passwordless.code.${reason}`)}
      </p>
    </div>
  );
}
