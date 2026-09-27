"use client";

import { useFormatter, useTranslations } from "next-intl";
import type { UrgencyTag } from "@/lib/applications/urgency";
import type { JpFormatter } from "@/lib/i18n/format";

/**
 * Kompakt datum UTAN år ("8 juli"): signalen fyrar bara ≤7 dagar kvar, så året
 * är alltid redundant (design-reviewer Minor 1, PR 7). null när datumet inte kan
 * tolkas.
 */
function shortDate(format: JpFormatter, iso: string): string | null {
  const parsed = new Date(iso);
  if (isNaN(parsed.getTime())) return null;
  return format.dateTime(parsed, { day: "numeric", month: "long" });
}

/**
 * Bråttom-taggens etikett (design §11) — strukturerad `UrgencyTag` → renderad
 * sträng via next-intl + useFormatter. SSOT delad av Lista-raden
 * (`application-row.tsx`) och Tavla-kortet (`application-board-card.tsx`) så de
 * två vyerna aldrig kan drifta isär i hur en bråttom-tagg formuleras (CLAUDE.md
 * §9.1 DRY). Returnerar null när taggen saknas eller datumet inte kan tolkas.
 */
export function useUrgencyLabel(urgency: UrgencyTag | null): string | null {
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();

  if (urgency == null) return null;
  switch (urgency.kind) {
    case "deadline": {
      const date = shortDate(format, urgency.dateIso);
      return date == null ? null : tUi("urgency.deadline", { date });
    }
    case "waitDays":
      return tUi("urgency.waitDays", { days: urgency.days });
    case "sinceInterview":
      return tUi("urgency.sinceInterview", { days: urgency.days });
  }
}

/**
 * The same urgency without its words, for the queue row, where the signal's kicker already
 * names it ("Väntar på svar" + "34 dagar", #1827 M1).
 */
export function useUrgencyValue(urgency: UrgencyTag | null): string | null {
  const tAttention = useTranslations("applications.ui.attention");
  const format = useFormatter();

  if (urgency == null) return null;
  switch (urgency.kind) {
    case "deadline":
      return shortDate(format, urgency.dateIso);
    case "waitDays":
    case "sinceInterview":
      return tAttention("days", { days: urgency.days });
  }
}
