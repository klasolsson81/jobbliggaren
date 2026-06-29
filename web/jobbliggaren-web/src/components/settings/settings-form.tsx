"use client";

import { useMemo, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { setLocaleAction } from "@/i18n/set-locale-action";
import {
  makeUpdateMyProfileSchema,
  type UpdateMyProfileInput,
} from "@/lib/actions/me-schemas";
import { updateMyProfileAction } from "@/lib/actions/me";
import type { JobSeekerProfileDto } from "@/lib/types/me";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import type { SkillGroup } from "@/lib/dto/skills";
import { PersonalInfoCard } from "./personal-info-card";
import { DisplayCard } from "./display-card";
import { BackgroundMatchCard } from "./background-match-card";
import { MatchPreferencesCard } from "./match-preferences-card";
import { PrivacyCard } from "./privacy-card";
import { LogoutCard } from "./logout-card";

interface SettingsFormProps {
  initialProfile: JobSeekerProfileDto;
  userEmail: string;
  /**
   * Taxonomi-trädet för matchnings-kortets väljare. `null` när
   * `getTaxonomyTree()` failade — kortet degraderar civilt (visar en lugn
   * "kunde inte läsas in"-text i stället för väljarna).
   */
  taxonomy: TaxonomyTree | null;
  /**
   * STEG 3 / ADR 0079 + ADR 0047 + #277: server-resolverade GRUPPER för de
   * sparade kompetens-concept-id. Seedar matchnings-kortets skill-grupp-store så
   * chips visar namn OCH EN chip per twin-par vid kall laddning (den platta
   * skill-taxonomin har ingen träd-uppslagning på FE). Tom lista vid fel →
   * graceful id-fallback kvarstår.
   */
  initialSkillGroups: ReadonlyArray<SkillGroup>;
}

type LanguageValue = "sv" | "en";

/**
 * SettingsForm — orchestrerar alla preferens-kort på /installningar.
 *
 * CTO-dom 2026-05-20 (F6 P2, Val 2B): EN form, EN action, kort som visuella
 * grupperingar. Klas-direktiv: Visning/Aviseringar är "direct-apply" — språk +
 * aviseringar applieras direkt via `updateMyProfileAction` vid varje ändring
 * (optimistic + revert vid fel). (MVP: tema-segmentet "släckt" — ett färgläge.)
 * Personuppgifter (Namn) har explicit "Spara ändringar"-knapp eftersom
 * text-input inte ska persistera per tangent.
 *
 * Race-condition-mitigering: action-anropen körs sekventiellt via
 * useTransition (en åt gången). Användare som klickar flera toggles snabbt
 * får senare anrop köade — den sista vinner. Snabbare flow kräver
 * cross-aggregate-locking som är out-of-scope (single-user-aggregate har
 * naturlig last-write-wins-semantik).
 *
 * FAS-DEFERRAL (Klas-godkänt 2026-05-20 + memory `feedback_design_reviewer_deferral_manifest`):
 *  - Telefon-fält INTE renderat (DTO saknar `phone`)
 *  - TD-115 (2026-06-25): the legacy "Aviseringar"-kort (EmailNotifications +
 *    WeeklySummary toggles) was REMOVED — those flags gated no email path and
 *    were retired. The live notification surface is BackgroundMatchCard below.
 *  - "Exportera mina data" + "Radera konto" hänvisar till befintliga flöden
 *    (DeleteAccountSection) eller stub-handler
 */
export function SettingsForm({
  initialProfile,
  userEmail,
  taxonomy,
  initialSkillGroups,
}: SettingsFormProps) {
  const t = useTranslations("validation");
  const ts = useTranslations("settings");
  const schema = useMemo(() => makeUpdateMyProfileSchema(t), [t]);
  const router = useRouter();
  const [displayName, setDisplayName] = useState(initialProfile.displayName);
  const [language, setLanguage] = useState<LanguageValue>(
    initialProfile.language === "en" ? "en" : "sv",
  );
  const [isPending, startTransition] = useTransition();
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  const [error, setError] = useState<string | null>(null);

  function buildPayload(
    overrides: Partial<UpdateMyProfileInput> = {},
  ): UpdateMyProfileInput {
    return {
      displayName,
      language,
      ...overrides,
    };
  }

  async function applyChange(
    overrides: Partial<UpdateMyProfileInput>,
    revert: () => void,
    onSuccess?: () => void | Promise<void>,
  ) {
    const payload = buildPayload(overrides);
    const parsed = schema.safeParse(payload);
    if (!parsed.success) {
      const first = parsed.error.issues[0];
      setError(first?.message ?? ts("account.invalidInput"));
      revert();
      return;
    }
    setError(null);
    startTransition(async () => {
      const result = await updateMyProfileAction(parsed.data);
      if (!result.success) {
        setError(result.error);
        revert();
      } else {
        setSavedAt(new Date());
        await onSuccess?.();
      }
    });
  }

  function onLanguageChange(next: LanguageValue) {
    const prev = language;
    setLanguage(next);
    // Flip the UI locale only after the profile save succeeds: the cookie is the
    // rendering source of truth (ADR 0078) and the profile is the durable backup,
    // so the two must stay in sync. Writing the cookie unconditionally would let
    // it win permanently on a save failure (the device sticks on the new locale
    // while the profile keeps the old one). Instead we revert local state and
    // leave the cookie untouched on failure.
    void applyChange({ language: next }, () => setLanguage(prev), async () => {
      await setLocaleAction(next);
      router.refresh();
    });
  }

  function onSavePersonalInfo(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    void applyChange({ displayName }, () =>
      setDisplayName(initialProfile.displayName),
    );
  }

  return (
    <div className="jp-settings-grid">
      <div className="jp-settings-grid__col">
        <PersonalInfoCard
          displayName={displayName}
          email={userEmail}
          isPending={isPending}
          error={error}
          savedAt={savedAt}
          onDisplayNameChange={setDisplayName}
          onSubmit={onSavePersonalInfo}
        />
        {/* F4-12 PR-B (ADR 0076): matchnings-önskemål. Kortet äger sin EGEN
            save (egen action/endpoint, egen useTransition) — INTE den delade
            applyChange/updateMyProfileSchema-flödet. `id="matchning"` på kortet
            ankrar nudge-länken /installningar#matchning. */}
        <MatchPreferencesCard
          occupationFields={taxonomy?.occupationFields ?? []}
          regions={taxonomy?.regions ?? []}
          employmentTypes={taxonomy?.employmentTypes ?? []}
          initialOccupationGroups={initialProfile.preferredOccupationGroups}
          initialRegions={initialProfile.preferredRegions}
          initialMunicipalities={initialProfile.preferredMunicipalities}
          initialEmploymentTypes={initialProfile.preferredEmploymentTypes}
          initialSkills={initialProfile.preferredSkills}
          initialSkillGroups={initialSkillGroups}
          initialExperienceYears={initialProfile.experienceYears}
          initialOccupationExperience={initialProfile.preferredOccupationExperience}
          degraded={taxonomy === null}
        />
      </div>

      <div className="jp-settings-grid__col">
        {/* MVP: tema-segmentet borttaget — appen har bara ETT färgläge (light).
            Dark-mode behålls dormant i koden (theme-provider DARK_MODE_ENABLED). */}
        <DisplayCard
          language={language}
          onLanguageChange={onLanguageChange}
          isPending={isPending}
        />
        {/* ADR 0080 Vag 4 PR-6: bakgrundsmatchnings-notiser (opt-in + kadens).
            Äger sin EGEN action/endpoint (PUT /me/notification-consent) — INTE
            det delade applyChange/updateMyProfile-flödet — så ett consent-spar
            aldrig blandas med profil-skrivet. Pre-fyller från profilen. */}
        <BackgroundMatchCard
          initialEnabled={initialProfile.backgroundMatchNotificationsEnabled}
          initialCadence={
            initialProfile.digestCadence === "Daily" ? "Daily" : "Weekly"
          }
        />
        <PrivacyCard userEmail={userEmail} />
        <LogoutCard />
      </div>
    </div>
  );
}
