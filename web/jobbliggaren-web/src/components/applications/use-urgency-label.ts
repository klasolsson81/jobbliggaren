"use client";

import { useFormatter, useTranslations } from "next-intl";
import type { UrgencyTag } from "@/lib/applications/urgency";

/**
 * Bråttom-taggens etikett (design §11) — strukturerad `UrgencyTag` → renderad
 * sträng via next-intl + useFormatter. SSOT delad av Lista-raden
 * (`application-row.tsx`) och Tavla-kortet (`application-board-card.tsx`) så de
 * två vyerna aldrig kan drifta isär i hur en bråttom-tagg formuleras (CLAUDE.md
 * §9.1 DRY). Kompakt datum UTAN år ("Deadline 8 juli"): signalen fyrar bara ≤7
 * dagar kvar, så året är alltid redundant (design-reviewer Minor 1, PR 7).
 * Returnerar null när taggen saknas eller datumet inte kan tolkas.
 */
export function useUrgencyLabel(urgency: UrgencyTag | null): string | null {
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();

  if (urgency == null) return null;
  switch (urgency.kind) {
    case "deadline": {
      const parsed = new Date(urgency.dateIso);
      if (isNaN(parsed.getTime())) return null;
      const date = format.dateTime(parsed, { day: "numeric", month: "long" });
      return tUi("urgency.deadline", { date });
    }
    case "waitDays":
      return tUi("urgency.waitDays", { days: urgency.days });
    case "sinceInterview":
      return tUi("urgency.sinceInterview", { days: urgency.days });
  }
}
