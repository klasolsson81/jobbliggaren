"use client";

// "use client": kortet kör en optimistisk save med useTransition runt server-
// actionen + revert-vid-fel, och visar en artig "Sparat"-kvittens / assertiv
// fel-alert. Inget av detta går i en Server Component.

import { useState, useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { formatTime } from "@/lib/i18n/format";
import type { DigestCadence } from "@/lib/dto/me";
import { updateFollowedCompanyNotificationConsentAction } from "@/lib/actions/me";
import { ToggleRow } from "@/components/ui/toggle-row";

interface FollowedCompanyNotificationsCardProps {
  /**
   * Sparat consent-läge (default OFF, GDPR Art. 6(1)(a)/7). KONTROLLERAT av
   * SettingsForm: kadensen är delad med matchningsnotiserna (ADR 0087 D2), och
   * BackgroundMatchCard måste veta om DEN HÄR kanalen är på för att kunna låta
   * användaren välja takt. Ett värde, en ägare (SSOT).
   */
  readonly enabled: boolean;
  /** Rapporterar upp både den optimistiska ändringen och en revert-vid-fel. */
  readonly onEnabledChange: (next: boolean) => void;
  /** Delad digest-kadens — visas som TEXT här, kontrollen bor i matchnings-kortet. */
  readonly cadence: DigestCadence;
}

/**
 * Bevakning F4 (#803) / CTO RF-12=12C — kort för notiser om företag du följer
 * på /installningar. Den KANONISKA Art. 7(3)-withdrawal-ytan för e-postkanalen:
 * innan det här kortet fanns hade flaggan ingen UI alls (en "mörk räl"), vilket
 * är vad kortet stänger — därför lanseringskritiskt, inte polish.
 *
 * SAMTYCKET GÄLLER ENDAST E-POST. Efter 7C (ADR 0087 D5 superseded) skapas
 * hits för ALLA som följer ett företag och in-app-rälen (Översikt + Företag)
 * går oavsett flaggan — det är tjänsten användaren begärt genom att följa
 * (Art. 6(1)(b)). Flaggan grindar e-postutskicket (Art. 6(1)(a), default OFF).
 * Introt SÄGER det: utan den meningen tror användaren att avstängt = inga
 * notiser alls (Art. 7(2)-transparens).
 *
 * INGEN kadens-kontroll här. Kadensen är DELAD med matchningsnotiserna
 * (ADR 0087 D2) och ägs av BackgroundMatchCard — två kontroller för ett värde
 * vore garanterad drift. Kortet visar i stället den gällande takten som text
 * och pekar på kortet där den ändras.
 *
 * Kortet äger sin EGEN save (egen action/endpoint
 * `PUT /me/followed-company-notification-consent`, egen useTransition) — INTE
 * det delade updateMyProfile-flödet, och inte matchnings-kortets endpoint.
 */
export function FollowedCompanyNotificationsCard({
  enabled,
  onEnabledChange,
  cadence,
}: FollowedCompanyNotificationsCardProps) {
  const t = useTranslations("settings");
  const format = useFormatter();

  const [isSaving, startSaving] = useTransition();
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const cadenceLabel =
    cadence === "Daily"
      ? t("backgroundMatch.cadenceDaily")
      : t("backgroundMatch.cadenceWeekly");

  function onToggle(nextEnabled: boolean) {
    const prevEnabled = enabled;
    setSaveError(null);
    onEnabledChange(nextEnabled);
    startSaving(async () => {
      const result = await updateFollowedCompanyNotificationConsentAction({
        enabled: nextEnabled,
      });
      if (result.success) {
        setSavedAt(new Date());
      } else {
        onEnabledChange(prevEnabled);
        setSaveError(result.error);
      }
    });
  }

  return (
    <section className="jp-card">
      <h2 className="jp-card__title">
        {t("followedCompanyNotifications.title")}
      </h2>
      <p className="text-body-sm text-text-primary">
        {t("followedCompanyNotifications.intro")}
      </p>

      <div className="mt-4">
        <ToggleRow
          label={t("followedCompanyNotifications.toggleLabel")}
          description={t("followedCompanyNotifications.toggleDescription")}
          checked={enabled}
          onChange={onToggle}
          disabled={isSaving}
        />
      </div>

      {/* Kadens-DISCLOSURE, inte en kontroll: takten är delad och ändras i
          matchnings-kortet. Namnger kortet, aldrig riktningen ("ovan") — ett
          riktningsord blir falskt om kolumnordningen ändras.

          Filter-noten skeppas i SAMMA våg som filter-affordansen den hänvisar
          till (F4b) — copy som pekar på en kontroll vi inte byggt är ett löfte
          vi bryter i samma andetag. */}
      <div className="jp-settings-field mt-4">
        <p className="jp-settings-field__hint">
          {enabled
            ? t("followedCompanyNotifications.cadenceNote", {
                cadence: cadenceLabel,
              })
            : t("followedCompanyNotifications.cadenceNoteDisabled")}
        </p>
        <p className="jp-settings-field__hint">
          {t("followedCompanyNotifications.filterNote")}
        </p>
      </div>

      {/* Ömsesidigt uteslutande live-regioner (paritet BackgroundMatchCard):
          fel = assertiv alert, annars artig status-kvittens. */}
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
              ? t("followedCompanyNotifications.savedAt", {
                  time: formatTime(format, savedAt),
                })
              : ""}
          </p>
        )}
      </div>
    </section>
  );
}
