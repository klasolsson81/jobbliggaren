"use client";

// "use client": kortet håller lokal consent-state (opt-in-toggle + kadens),
// kör en optimistisk save med useTransition runt server-actionen + revert-vid-
// fel, och visar en artig "Sparat"-kvittens / assertiv fel-alert. Inget av
// detta går i en Server Component.

import { useState, useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { formatTime } from "@/lib/i18n/format";
import type { DigestCadence } from "@/lib/dto/me";
import { updateNotificationConsentAction } from "@/lib/actions/me";
import { ToggleRow } from "@/components/ui/toggle-row";
import { Segment, type SegmentOption } from "@/components/ui/segment";

interface BackgroundMatchCardProps {
  /** Sparat consent-läge från profilen (default OFF, GDPR Art. 6/7). */
  readonly initialEnabled: boolean;
  /** Sparad digest-kadens (default Weekly; meningsfull endast när enabled). */
  readonly initialCadence: DigestCadence;
}

/**
 * ADR 0080 Vag 4 PR-6 — kort för bakgrundsmatchnings-notiser på /installningar.
 *
 * En PASSIV inställnings-affordans (aldrig en banner/nag): användaren väljer
 * själv att slå PÅ bakgrundsmatchning (opt-in, default OFF — GDPR Art. 6(1)(a)/
 * 7) och kan dra tillbaka samtycket när som helst (slå av). När påslaget kör
 * bakgrundsmatchningen nattetid: Topp-matchningar skickas direkt via e-post,
 * Stark-matchningar samlas i en e-postsammanfattning enligt vald kadens (Daglig/
 * Veckovis), Bra-matchningar visas i matchningslistan utan e-post. Graden är en
 * NAMNGIVEN kategori, aldrig en siffra (ADR 0071, Goodhart). Copy:n namnger
 * e-post som leveranskanal (GDPR Art. 7(2) transparens, TD-116; Vag 4 PR-4b
 * skickar riktiga notiser via Resend).
 *
 * Kortet äger sin EGEN save (egen action/endpoint `PUT /me/notification-consent`,
 * egen useTransition) — INTE det delade `updateMyProfileAction`-flödet. Toggle
 * och kadens-byte skickar båda HELA `{enabled, cadence}` (idempotent full-
 * replace). Kadens-väljaren är alltid renderad men `disabled` när toggeln är av
 * (det inaktiva läget annonseras av Segment via `aria-disabled`; bättre a11y än
 * att dölja den, och hjälptexten förklarar att den används först när påslaget).
 */
export function BackgroundMatchCard({
  initialEnabled,
  initialCadence,
}: BackgroundMatchCardProps) {
  const t = useTranslations("settings");
  const format = useFormatter();

  // Optimistisk lokal state som reverteras vid fel — varje ändring sparas direkt.
  const [enabled, setEnabled] = useState<boolean>(initialEnabled);
  const [cadence, setCadence] = useState<DigestCadence>(initialCadence);
  const [isSaving, startSaving] = useTransition();
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const cadenceOptions: ReadonlyArray<SegmentOption<DigestCadence>> = [
    { value: "Daily", label: t("backgroundMatch.cadenceDaily") },
    { value: "Weekly", label: t("backgroundMatch.cadenceWeekly") },
  ];

  /** Persisterar HELA consent-läget (full-replace) med revert-vid-fel. */
  function persist(
    next: { enabled: boolean; cadence: DigestCadence },
    revert: () => void
  ) {
    setSaveError(null);
    startSaving(async () => {
      const result = await updateNotificationConsentAction(next);
      if (result.success) {
        setSavedAt(new Date());
      } else {
        revert();
        setSaveError(result.error);
      }
    });
  }

  function onToggle(nextEnabled: boolean) {
    const prevEnabled = enabled;
    setEnabled(nextEnabled);
    persist({ enabled: nextEnabled, cadence }, () => setEnabled(prevEnabled));
  }

  function onCadenceChange(nextCadence: DigestCadence) {
    const prevCadence = cadence;
    setCadence(nextCadence);
    persist({ enabled, cadence: nextCadence }, () => setCadence(prevCadence));
  }

  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{t("backgroundMatch.title")}</h2>
      <p className="text-body-sm text-text-primary">
        {t("backgroundMatch.intro")}
      </p>

      <div className="mt-4">
        <ToggleRow
          label={t("backgroundMatch.toggleLabel")}
          description={t("backgroundMatch.toggleDescription")}
          checked={enabled}
          onChange={onToggle}
          disabled={isSaving}
        />
      </div>

      <div className="jp-settings-field mt-4">
        <span className="jp-settings-field__label">
          {t("backgroundMatch.cadenceLabel")}
        </span>
        <Segment
          aria-label={t("backgroundMatch.cadenceLabel")}
          value={cadence}
          onChange={onCadenceChange}
          options={cadenceOptions}
          disabled={!enabled || isSaving}
        />
        <p className="jp-settings-field__hint">
          {enabled
            ? t("backgroundMatch.cadenceHint")
            : t("backgroundMatch.cadenceHintDisabled")}
        </p>
      </div>

      {/* Ömsesidigt uteslutande live-regioner (samma mönster som matchnings-
          kortet): fel = assertiv alert, annars artig status-kvittens. */}
      <div className="mt-4">
        {saveError ? (
          <p role="alert" className="text-body-sm text-danger-600">
            {saveError}
          </p>
        ) : (
          <p
            role="status"
            aria-live="polite"
            className="text-body-sm text-text-secondary"
          >
            {!isSaving && savedAt
              ? t("backgroundMatch.savedAt", {
                  time: formatTime(format, savedAt),
                })
              : ""}
          </p>
        )}
      </div>
    </section>
  );
}
