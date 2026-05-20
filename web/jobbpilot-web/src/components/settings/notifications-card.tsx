"use client";

import { ToggleRow } from "@/components/ui/toggle-row";

interface NotificationsCardProps {
  emailNotifications: boolean;
  weeklySummary: boolean;
  onEmailNotificationsChange: (next: boolean) => void;
  onWeeklySummaryChange: (next: boolean) => void;
  isPending: boolean;
}

/**
 * Aviseringar-kort. CTO 2026-05-20 Val 3B: visa bara wirade prefs.
 * `JobSeekerProfileDto` har idag två fält — `emailNotifications` (generell
 * e-post på/av) + `weeklySummary` (sammanfattning på e-post). Klas-promptens
 * 4 strängar ("Nya matchningar", "Påminnelser", "Statusändringar",
 * "Veckosammanfattning") reduceras tills DTO utökas i framtida fas.
 *
 * Direct-apply per Klas-direktiv: varje toggle anropar
 * `updateMyProfileAction` direkt med optimistic update + revert vid fel.
 */
export function NotificationsCard({
  emailNotifications,
  weeklySummary,
  onEmailNotificationsChange,
  onWeeklySummaryChange,
  isPending,
}: NotificationsCardProps) {
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">Aviseringar</h2>
      <ToggleRow
        label="E-postnotifikationer"
        description="Få mejl vid viktiga händelser i ditt konto."
        checked={emailNotifications}
        onChange={onEmailNotificationsChange}
        disabled={isPending}
      />
      <ToggleRow
        label="Veckosammanfattning via e-post"
        description="Sammanfattning av dina ansökningar och nya annonser."
        checked={weeklySummary}
        onChange={onWeeklySummaryChange}
        disabled={isPending}
      />
    </section>
  );
}
