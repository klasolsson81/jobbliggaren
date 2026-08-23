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
import type { DigestCadence } from "@/lib/dto/me";
import { PersonalInfoCard } from "./personal-info-card";
import { DisplayCard } from "./display-card";
import { BackgroundMatchCard } from "./background-match-card";
import { FollowedCompanyNotificationsCard } from "./followed-company-notifications-card";
import { MatchPreferencesCard } from "./match-preferences-card";
import { ChangeEmailCard } from "./change-email-card";
import { ChangePasswordCard } from "./change-password-card";
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
 * #1391 — the outcome of ONE write, owned by the control that started it.
 *
 * A union rather than separate error/savedAt fields: a card can never render a receipt and
 * a failure at the same time, so the mutual exclusion is structural instead of a `!error`
 * guard the next reader has to find. `field` carries the action's own opt-in discriminator
 * (#1117); null means the failure names no input.
 */
type WriteOutcome =
  | { ok: true; at: Date }
  | { ok: false; error: string; field: "displayName" | null };

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
  // One outcome per DIRECT-APPLY control, not one per form. `applyChange` is shared by the
  // language segment and the name form, and a single set of signals meant either one's
  // result rendered in the Personuppgifter card — the failure silently, since the language
  // segment reverts, and the receipt permanently, on a card the user never touched.
  const [nameOutcome, setNameOutcome] = useState<WriteOutcome | null>(null);
  const [languageOutcome, setLanguageOutcome] = useState<WriteOutcome | null>(null);

  /**
   * Bevakning F4 (#803): de två notis-kortens DELADE tillstånd bor här, inte i
   * korten. Kadensen driver båda utskicken (ADR 0087 D2) och följ-flaggan avgör
   * om kadens-väljaren är åtkomlig — så båda värdena har två läsare och därmed
   * exakt EN ägare (SSOT). Varje kort behåller sin EGEN save/endpoint; det är
   * bara sanningen som är delad, aldrig skrivvägen.
   */
  const [cadence, setCadence] = useState<DigestCadence>(
    initialProfile.digestCadence === "Daily" ? "Daily" : "Weekly",
  );
  const [followEnabled, setFollowEnabled] = useState<boolean>(
    initialProfile.followedCompanyNotificationsEnabled,
  );

  // `changed` is the WHOLE payload: only the fields this caller is changing. Sending the
  // unchanged ones is not free — since #1117 the display name carries a server-side invariant
  // re-evaluated on every write, so a profile row written before that invariant landed would
  // have its LANGUAGE change refused on the strength of a name the user never touched. The
  // command is a partial update (the handler applies each field only when non-null), so what
  // goes over the wire is exactly what changed. There is deliberately no buildPayload() wrapper
  // between the two: after the fix it was an identity function whose default argument no call
  // site used, and an accidental no-argument call would have PATCHed nothing while still
  // stamping "Sparat".
  async function applyChange(
    changed: Partial<UpdateMyProfileInput>,
    revert: () => void,
    report: (outcome: WriteOutcome | null) => void,
    onSuccess?: () => void | Promise<void>,
  ) {
    const parsed = schema.safeParse(changed);
    if (!parsed.success) {
      const first = parsed.error.issues[0];
      report({
        ok: false,
        error: first?.message ?? ts("account.invalidInput"),
        field: first?.path[0] === "displayName" ? "displayName" : null,
      });
      revert();
      return;
    }
    report(null);
    startTransition(async () => {
      const result = await updateMyProfileAction(parsed.data);
      if (!result.success) {
        report({ ok: false, error: result.error, field: result.field ?? null });
        revert();
      } else {
        report({ ok: true, at: new Date() });
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
    void applyChange(
      { language: next },
      () => setLanguage(prev),
      setLanguageOutcome,
      async () => {
        await setLocaleAction(next);
        router.refresh();
      },
    );
  }

  function onSavePersonalInfo(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    void applyChange(
      { displayName },
      () => setDisplayName(initialProfile.displayName),
      setNameOutcome,
    );
  }

  const nameFailure = nameOutcome?.ok === false ? nameOutcome : null;
  const languageFailure = languageOutcome?.ok === false ? languageOutcome : null;

  return (
    <div className="jp-settings-grid">
      <div className="jp-settings-grid__col">
        <PersonalInfoCard
          displayName={displayName}
          email={userEmail}
          isPending={isPending}
          error={nameFailure?.error ?? null}
          errorField={nameFailure?.field ?? null}
          savedAt={nameOutcome?.ok === true ? nameOutcome.at : null}
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
          initialRemote={initialProfile.preferredRemote}
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
          error={languageFailure?.error ?? null}
          savedAt={languageOutcome?.ok === true ? languageOutcome.at : null}
        />
        {/* ADR 0080 Vag 4 PR-6: bakgrundsmatchnings-notiser (opt-in + kadens).
            Äger sin EGEN action/endpoint (PUT /me/notification-consent) — INTE
            det delade applyChange/updateMyProfile-flödet — så ett consent-spar
            aldrig blandas med profil-skrivet. Pre-fyller från profilen.
            Bevakning F4: kadensen är delad (D2) → kontrollerad härifrån, och
            väljaren öppnas så snart någon av de två kanalerna är på. */}
        <BackgroundMatchCard
          initialEnabled={initialProfile.backgroundMatchNotificationsEnabled}
          cadence={cadence}
          onCadenceChange={setCadence}
          followEnabled={followEnabled}
        />
        {/* Bevakning F4 (#803, RF-12=12C): notiser om följda företag. Den
            kanoniska Art. 7(3)-withdrawal-ytan för E-POST-kanalen (in-app-rälen
            går oavsett — 6(1)(b) efter 7C). Egen action/endpoint
            (PUT /me/followed-company-notification-consent). Placerad direkt
            efter matchnings-kortet: de två delar kadens, och adjacensen är vad
            som gör kadens-hänvisningen hittbar även när gridden kollapsar till
            en kolumn. INGEN kadens-kontroll här (ett värde, en kontroll). */}
        <FollowedCompanyNotificationsCard
          enabled={followEnabled}
          onEnabledChange={setFollowEnabled}
          cadence={cadence}
        />
        {/* #679 — self-service change-email (request step). Owns its own
            action/endpoint (POST /auth/change-email), not the shared
            applyChange/updateMyProfile flow. Placed before change-password so the
            two credential cards read identity -> secret. */}
        <ChangeEmailCard currentEmail={userEmail} />
        {/* #678 — self-service change-password + C6 (logout-everywhere + re-issue
            this device). Owns its own action/endpoint (POST /auth/change-password),
            not the shared applyChange/updateMyProfile flow. */}
        <ChangePasswordCard />
        <PrivacyCard userEmail={userEmail} />
        <LogoutCard />
      </div>
    </div>
  );
}
