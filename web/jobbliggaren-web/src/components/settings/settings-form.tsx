"use client";

import { useMemo, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Moon, Sun } from "lucide-react";
import { useTranslations } from "next-intl";
import { setLocaleAction } from "@/i18n/set-locale-action";
import { useTheme } from "@/components/theme-provider";
import {
  makeUpdateMyProfileSchema,
  type UpdateMyProfileInput,
} from "@/lib/actions/me-schemas";
import { updateMyProfileAction } from "@/lib/actions/me";
import type { JobSeekerProfileDto } from "@/lib/types/me";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import type { Option } from "./match-preferences-shared";
import { PersonalInfoCard } from "./personal-info-card";
import { DisplayCard } from "./display-card";
import { NotificationsCard } from "./notifications-card";
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
   * STEG 3 / ADR 0079 + ADR 0047: server-resolverade labels för de sparade
   * kompetens-concept-id. Seedar matchnings-kortets skill-label-store så chips
   * visar namn vid kall laddning (den platta skill-taxonomin har ingen
   * träd-uppslagning på FE). Tom lista vid fel → graceful id-fallback kvarstår.
   */
  initialSkillLabels: ReadonlyArray<Option>;
}

type LanguageValue = "sv" | "en";

/**
 * SettingsForm — orchestrerar alla preferens-kort på /installningar.
 *
 * CTO-dom 2026-05-20 (F6 P2, Val 2B): EN form, EN action, kort som visuella
 * grupperingar. Klas-direktiv: Visning/Aviseringar är "direct-apply" — tema
 * ändras lokalt via useTheme (ingen backend), språk + aviseringar applieras
 * direkt via `updateMyProfileAction` vid varje ändring (optimistic + revert
 * vid fel). Personuppgifter (Namn) har explicit "Spara ändringar"-knapp
 * eftersom text-input inte ska persistera per tangent.
 *
 * Race-condition-mitigering: action-anropen körs sekventiellt via
 * useTransition (en åt gången). Användare som klickar flera toggles snabbt
 * får senare anrop köade — den sista vinner. Snabbare flow kräver
 * cross-aggregate-locking som är out-of-scope (single-user-aggregate har
 * naturlig last-write-wins-semantik).
 *
 * FAS-DEFERRAL (Klas-godkänt 2026-05-20 + memory `feedback_design_reviewer_deferral_manifest`):
 *  - Telefon-fält INTE renderat (DTO saknar `phone`)
 *  - Aviseringar = 2 wirede toggles ("E-postnotifikationer" + "Veckosammanfattning")
 *    — Klas-promptens 4 strängar reducerad till 2 (no-mock-doktrin)
 *  - "Exportera mina data" + "Radera konto" hänvisar till befintliga flöden
 *    (DeleteAccountSection) eller stub-handler
 */
export function SettingsForm({
  initialProfile,
  userEmail,
  taxonomy,
  initialSkillLabels,
}: SettingsFormProps) {
  const t = useTranslations("validation");
  const ts = useTranslations("settings");
  const schema = useMemo(() => makeUpdateMyProfileSchema(t), [t]);
  const router = useRouter();
  const { theme, setTheme } = useTheme();
  const [displayName, setDisplayName] = useState(initialProfile.displayName);
  const [language, setLanguage] = useState<LanguageValue>(
    initialProfile.language === "en" ? "en" : "sv",
  );
  const [emailNotifications, setEmailNotifications] = useState(
    initialProfile.emailNotifications,
  );
  const [weeklySummary, setWeeklySummary] = useState(
    initialProfile.weeklySummary,
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
      emailNotifications,
      weeklySummary,
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

  function onEmailNotificationsChange(next: boolean) {
    const prev = emailNotifications;
    setEmailNotifications(next);
    void applyChange({ emailNotifications: next }, () =>
      setEmailNotifications(prev),
    );
  }

  function onWeeklySummaryChange(next: boolean) {
    const prev = weeklySummary;
    setWeeklySummary(next);
    void applyChange({ weeklySummary: next }, () => setWeeklySummary(prev));
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
          initialSkillLabels={initialSkillLabels}
          initialExperienceYears={initialProfile.experienceYears}
          initialOccupationExperience={initialProfile.preferredOccupationExperience}
          degraded={taxonomy === null}
        />
      </div>

      <div className="jp-settings-grid__col">
        <DisplayCard
          theme={theme === "dark" ? "dark" : "light"}
          onThemeChange={setTheme}
          language={language}
          onLanguageChange={onLanguageChange}
          isPending={isPending}
          themeOptions={[
            { value: "light", label: ts("display.themeLight"), icon: <Sun size={16} /> },
            { value: "dark", label: ts("display.themeDark"), icon: <Moon size={16} /> },
          ]}
        />
        <NotificationsCard
          emailNotifications={emailNotifications}
          weeklySummary={weeklySummary}
          onEmailNotificationsChange={onEmailNotificationsChange}
          onWeeklySummaryChange={onWeeklySummaryChange}
          isPending={isPending}
        />
        <PrivacyCard userEmail={userEmail} />
        <LogoutCard />
      </div>
    </div>
  );
}
