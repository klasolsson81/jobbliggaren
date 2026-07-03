"use client";

// "use client": liten klient-ö på /cv som äger wizardens öppna-state, en
// dismissbar "uppdatera matchning?"-prompt (lokal state) och adopterar den
// sparade mängden efter en lyckad save. Server-komponenten (/cv) hämtar
// taxonomi + persisterade preferenser och skickar in dem som serialiserbara
// props. Inget av detta går i en Server Component.

import { useState } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { SkillGroup } from "@/lib/dto/skills";
import { MatchSetupRailModal } from "@/components/settings/match-setup-rail-modal";

interface CvMatchSetupProps {
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
  /** Spår 3 PR-D: kommun-axeln (pre-fill för wizardens ort-steg). */
  readonly persistedMunicipalities: ReadonlyArray<string>;
  readonly persistedEmploymentTypes: ReadonlyArray<string>;
  /** STEG 3 / ADR 0079: kompetens-axeln (pre-fill för wizardens kompetens-steg). */
  readonly persistedSkills: ReadonlyArray<string>;
  /**
   * #422 (#253/#277 group-resolution): reverse-resolved GROUPS for the saved
   * skill ids, resolved server-side in /cv (`resolveSkillLabels`, mirroring
   * `/installningar`). Forwarded straight to the wizard so a returning user's
   * saved skills render Swedish labels, not raw concept-ids, on a cold load
   * (the #422 defect). Optional + defaults to `[]`; the wizard keeps its
   * graceful id-fallback if unresolved.
   *
   * NOT adopted into local state: the wizard's `onSaved` contract carries only
   * the flat saved skill ids (no groups), and `updateMatchPreferencesAction`
   * revalidates only /installningar + /oversikt — never /cv. So, unlike the
   * /installningar card (which adopts `saved.skillGroups`), CvMatchSetup cannot
   * refresh these groups after an in-page save without a contract change. A
   * skill added via the wizard's search and then re-viewed without a full page
   * reload may fall back to its concept-id; that after-save-reopen gap is out of
   * scope for #422, which is the cold-load path.
   */
  readonly persistedSkillGroups?: ReadonlyArray<SkillGroup>;
  /** exp-per-occ (ADR 0079-amendment PR-4): per-yrke-erfarenhets-overlay (pre-fill). */
  readonly persistedOccupationExperience: ReadonlyArray<{
    readonly conceptId: string;
    readonly years: number | null;
  }>;
  /** CV-importflödets route (yrkes-stegets tom-state-länk). */
  readonly importCvHref: string;
  /**
   * `true` när användaren redan angett minst en preferens → trigger-copy blir
   * "Uppdatera matchning" i stället för "Skapa matchning från ditt CV".
   * (`hasStatedDesiredOccupation` från profilen.)
   */
  readonly hasPreferences: boolean;
  /**
   * `true` direkt efter en CV-uppladdning/promote → visar den dismissbara
   * "Vill du uppdatera din matchning?"-prompten (design C.3). Server-sidan
   * sätter den via en sökparameter; annars dold.
   */
  readonly showPrompt: boolean;
}

/**
 * /cv match-setup-affordans: en trigger i innehållsytan (.jp-cvmatch-bar, ej
 * gradient-hero-asiden) som öppnar match-setup-wizarden, plus den dismissbara
 * post-promote-prompten. Skriver inget själv — wizarden bär det enda PUT:et
 * (MatchPreferences).
 */
export function CvMatchSetup({
  occupationFields,
  regions,
  employmentTypes,
  persistedOccupationGroups,
  persistedRegions,
  persistedMunicipalities,
  persistedEmploymentTypes,
  persistedSkills,
  persistedSkillGroups = [],
  persistedOccupationExperience,
  importCvHref,
  hasPreferences,
  showPrompt,
}: CvMatchSetupProps) {
  const t = useTranslations("resumes.match");
  const [open, setOpen] = useState(false);
  const [promptDismissed, setPromptDismissed] = useState(false);

  // Adoptera den sparade mängden lokalt så trigger-copy + prompt blir koherenta
  // direkt efter save (revalidatePath om-renderar RSC men byter inte useState).
  const [occupations, setOccupations] = useState(persistedOccupationGroups);
  const [regionPrefs, setRegionPrefs] = useState(persistedRegions);
  const [municipalityPrefs, setMunicipalityPrefs] = useState(
    persistedMunicipalities
  );
  const [employmentPrefs, setEmploymentPrefs] = useState(persistedEmploymentTypes);
  const [skillPrefs, setSkillPrefs] = useState(persistedSkills);
  // exp-per-occ (ADR 0079-amendment PR-4): per-yrke-erfarenhets-overlay.
  const [occupationExperiencePref, setOccupationExperiencePref] = useState(
    persistedOccupationExperience
  );
  const [savedAny, setSavedAny] = useState(false);

  const hasAny = savedAny ? occupations.length > 0 : hasPreferences;
  const triggerLabel = hasAny
    ? t("triggerUpdate")
    : t("triggerCreate");

  return (
    <>
      <Button
        type="button"
        variant="secondary"
        aria-haspopup="dialog"
        onClick={() => setOpen(true)}
      >
        {triggerLabel}
      </Button>

      {showPrompt && !promptDismissed && (
        <section
          className="jp-matchnudge"
          role="region"
          aria-labelledby="cv-match-nudge-title"
        >
          <div className="jp-matchnudge__body">
            <p id="cv-match-nudge-title" className="jp-matchnudge__title">
              {t("nudgeTitle")}
            </p>
            <p className="jp-matchnudge__text">{t("nudgeText")}</p>
            <div className="jp-matchnudge__actions">
              <Button type="button" onClick={() => setOpen(true)}>
                {t("nudgeAction")}
              </Button>
            </div>
          </div>
          <button
            type="button"
            className="jp-matchnudge__dismiss"
            aria-label={t("nudgeDismiss")}
            onClick={() => setPromptDismissed(true)}
          >
            <X size={16} aria-hidden="true" />
          </button>
        </section>
      )}

      <MatchSetupRailModal
        open={open}
        onOpenChange={setOpen}
        // /cv: användaren har redan ett CV och uppdaterar sin matchning → öppna
        // på Yrken (steg 1), inte det redundanta välkomst-/upload-steget. CV-
        // förslag kommer från det befordrade CV:t (autoSuggestFromCv).
        initialStep={1}
        occupationFields={occupationFields}
        regions={regions}
        employmentTypes={employmentTypes}
        persistedOccupationGroups={occupations}
        persistedRegions={regionPrefs}
        persistedMunicipalities={municipalityPrefs}
        persistedEmploymentTypes={employmentPrefs}
        persistedSkills={skillPrefs}
        persistedSkillGroups={persistedSkillGroups}
        persistedOccupationExperience={occupationExperiencePref}
        importCvHref={importCvHref}
        onSaved={(saved) => {
          setOccupations(saved.occupations);
          setRegionPrefs(saved.regions);
          setMunicipalityPrefs(saved.municipalities);
          setEmploymentPrefs(saved.employment);
          setSkillPrefs(saved.skills);
          setOccupationExperiencePref(saved.occupationExperience);
          setSavedAny(true);
          setPromptDismissed(true);
        }}
      />
    </>
  );
}
