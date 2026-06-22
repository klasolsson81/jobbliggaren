"use client";

// "use client": liten klient-ö på /cv som äger wizardens öppna-state, en
// dismissbar "uppdatera matchning?"-prompt (lokal state) och adopterar den
// sparade mängden efter en lyckad save. Server-komponenten (/cv) hämtar
// taxonomi + persisterade preferenser och skickar in dem som serialiserbara
// props. Inget av detta går i en Server Component.

import { useState } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import { MatchSetupWizard } from "@/components/settings/match-setup-wizard";

interface CvMatchSetupProps {
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
  /** Spår 3 PR-D: kommun-axeln (pre-fill för wizardens ort-steg). */
  readonly persistedMunicipalities: ReadonlyArray<string>;
  readonly persistedEmploymentTypes: ReadonlyArray<string>;
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
  importCvHref,
  hasPreferences,
  showPrompt,
}: CvMatchSetupProps) {
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
  const [savedAny, setSavedAny] = useState(false);

  const hasAny = savedAny ? occupations.length > 0 : hasPreferences;
  const triggerLabel = hasAny
    ? "Uppdatera matchning"
    : "Skapa matchning från ditt CV";

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
              Vill du uppdatera din matchning utifrån det här CV:t?
            </p>
            <p className="jp-matchnudge__text">
              Vi kan föreslå yrken utifrån ditt CV. Du väljer själv vad som tas
              med, och kan hoppa över det.
            </p>
            <div className="jp-matchnudge__actions">
              <Button type="button" onClick={() => setOpen(true)}>
                Ställ in matchning
              </Button>
            </div>
          </div>
          <button
            type="button"
            className="jp-matchnudge__dismiss"
            aria-label="Stäng"
            onClick={() => setPromptDismissed(true)}
          >
            <X size={16} aria-hidden="true" />
          </button>
        </section>
      )}

      <MatchSetupWizard
        open={open}
        onOpenChange={setOpen}
        occupationFields={occupationFields}
        regions={regions}
        employmentTypes={employmentTypes}
        persistedOccupationGroups={occupations}
        persistedRegions={regionPrefs}
        persistedMunicipalities={municipalityPrefs}
        persistedEmploymentTypes={employmentPrefs}
        importCvHref={importCvHref}
        onSaved={(saved) => {
          setOccupations(saved.occupations);
          setRegionPrefs(saved.regions);
          setMunicipalityPrefs(saved.municipalities);
          setEmploymentPrefs(saved.employment);
          setSavedAny(true);
          setPromptDismissed(true);
        }}
      />
    </>
  );
}
