"use client";

// "use client": yrkes-sektionen håller filter-/aktiv-kolumn-state, en CV-suggest
// (pending/diskriminerat resultat-state) och en disclosure-toggle för manuell
// "Lägg till yrken"-kaskaden. Extraherad ur match-preferences-dialog (ADR 0077
// STEG 5) och delad med match-setup-wizard. INGEN AI (deterministisk, ADR 0071);
// CV-förslag PRE-ADDAS till draften (chips) men skrivs ALDRIG till servern förrän
// värdens "Spara matchning" (propose-and-approve, ADR 0040 Beslut 4 / 0076).

import { useEffect, useId, useMemo, useRef, useState, useTransition } from "react";
import { ChevronRight, Plus } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { CvUploadForm } from "@/components/resumes/cv-upload-form";
import type { TaxonomyOccupationField } from "@/lib/dto/taxonomy";
import {
  suggestOccupationsFromCvAction,
  suggestOccupationsFromParsedResumeAction,
  type CvSuggestResult,
} from "@/lib/actions/match-preferences";
import {
  filterOptions,
  flattenOccupationGroups,
  labelsForSelected,
} from "./match-preferences-shared";
import { CheckItem, PinnedChips } from "./section-helpers";

interface OccupationSectionProps {
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  /** Valda yrkesgrupp-concept-id (draft). */
  readonly selected: ReadonlyArray<string>;
  /** Toggla ett yrkesgrupp-concept-id i draften (chip-borttagning + kaskad-rad). */
  readonly onToggle: (conceptId: string) => void;
  /**
   * Ersätt hela yrkes-valet (draft). Används för CV-förslagets pre-add (merge av
   * kandidater in i draften) — chips, inte en separat kryss-checklista. Behövs
   * eftersom pre-add skriver flera id på en gång; `onToggle` är en-i-taget.
   */
  readonly onReplace: (next: string[]) => void;
  /** Töm yrkes-valet helt. */
  readonly onClear: () => void;
  /** CV-importflödets route (tom-state-länken i CV-förslaget). */
  readonly importCvHref: string;
  /**
   * Unik DOM-id-prefix så sektionen kan monteras i flera värdar utan
   * id-kollision (dialog vs wizard). Default behåller dialogens tidigare id:n.
   */
  readonly idPrefix?: string;
  /** rubrik-id som värden kopplar `aria-labelledby` mot (för role=group). */
  readonly headingId?: string;
  /**
   * Visa sektionens egna "Yrken"-rubrik. Default true (dialogen). Wizarden sätter
   * false — där bär DialogTitle ("Yrken") rubriken, och en andra inline-rubrik
   * vore en dubblett. När false renderas bara Rensa-länken (när något är valt).
   */
  readonly showHeading?: boolean;
  /**
   * Wizard-prefill: kör CV-förslaget automatiskt när sektionen monteras (en
   * gång). Förslagen PRE-ADDAS till draften (chips) — de skrivs ALDRIG till
   * servern (propose-and-approve). I dialogen är detta `false` (knapp-driven).
   */
  readonly autoSuggestFromCv?: boolean;
  /**
   * Fas 4 onboarding (CTO Variant B): id för det just uppladdade `parsed_resume`:t
   * (welcome-flödet). När satt läses CV-förslaget ur den staging-artefakten
   * (`occupation_proposals`, ingen DEK/CV-PII) i stället för ur det promotade
   * `Resume`:ts `latestRole` — en ny användare har ännu inget promotat Resume.
   * Utelämnat (dialog/`/cv`/`/installningar`) → faller tillbaka på latestRole-vägen.
   */
  readonly parsedResumeId?: string;
}

/**
 * YRKEN-sektionen: pinnade chips (inkl. CV-förslag pre-addade) + EN tydlig
 * "Lägg till yrken"-CTA som öppnar en inline-disclosure med
 * filter/tvåkolumns-kaskad. Den rikaste preferens-sektionen. Återanvänds av
 * BÅDE match-preferences-dialog och match-setup-wizard.
 */
export function OccupationSection({
  occupationFields,
  selected,
  onToggle,
  onReplace,
  onClear,
  importCvHref,
  idPrefix = "match-dialog",
  headingId,
  showHeading = true,
  autoSuggestFromCv = false,
  parsedResumeId,
}: OccupationSectionProps) {
  const t = useTranslations("settings");
  const occupationOptions = useMemo(
    () => flattenOccupationGroups(occupationFields),
    [occupationFields]
  );

  const [occupationFilter, setOccupationFilter] = useState("");
  const [activeField, setActiveField] = useState<string | null>(null);

  // Manuell "Lägg till yrken"-disclosure (kollapsad tills CTA:n klickas — inget
  // "skrivs ut på en gång"). Inline-kaskad i stället för popover: en
  // absolut-positionerad popover skulle misspositioneras + slåss med Radix
  // Dialogens fokus-trap/Esc inuti modalen (se rapport). Disclosuren bär samma
  // markup som tidigare inline-kaskad, dold bakom EN knapp.
  const [pickerOpen, setPickerOpen] = useState(false);
  const panelRef = useRef<HTMLDivElement | null>(null);

  // CV-förslag (pending/diskriminerat). noCv/noRole/error/unauthorized visas
  // inline; "candidates" pre-addas till draften (chips) i stället för att
  // renderas som en separat kryss-checklista.
  const [cvResult, setCvResult] = useState<CvSuggestResult | null>(null);
  const [isCvSuggesting, startCvSuggest] = useTransition();

  // Spår 4 (Klas 2026-06-21): "Inget CV uppladdat"-staten laddar upp CV:t INLINE
  // (CvUploadForm i samma modal) i stället för att navigera bort till
  // /cv/importera-sidan. INGEN nästlad Radix-Dialog — CvUploadForm är ett vanligt
  // formulär (samma inline-användning som welcome-modalen), så ingen orphan-overlay.
  // localParsedId = det just inline-uppladdade parsed_resume:t (welcome-flödets
  // parsedResumeId-prop tar fortfarande företräde).
  const [uploadOpen, setUploadOpen] = useState(false);
  const [localParsedId, setLocalParsedId] = useState<string | null>(null);

  function runCvSuggest(explicitParsedId?: string) {
    // Prioritet: ett just inline-uppladdat CV (explicit) → welcome-flödets prop →
    // ett tidigare inline-uppladdat → annars promotade Resume:ts latestRole.
    const parsedId = explicitParsedId ?? parsedResumeId ?? localParsedId;
    setCvResult(null);
    startCvSuggest(async () => {
      const result = parsedId
        ? await suggestOccupationsFromParsedResumeAction(parsedId)
        : await suggestOccupationsFromCvAction();
      setCvResult(result);
      // PRE-ADD: kandidaterna läggs till draften som borttagbara chips
      // (propose-and-approve — draft-only, inget skrivs till servern). Merge med
      // befintligt val, dedupe (om användaren redan valt något manuellt).
      if (result.kind === "candidates" && result.candidates.length > 0) {
        const candidateIds = result.candidates.map(
          (c) => c.occupationGroupConceptId
        );
        onReplace([...new Set([...selected, ...candidateIds])]);
      }
    });
  }

  // Inline-uppladdning klar: behåll id:t, stäng upload-ytan och kör CV-förslaget
  // direkt mot det nya parsed_resume:t (samma propose-and-approve som annars).
  function handleCvUploaded(parsedId: string) {
    setLocalParsedId(parsedId);
    setUploadOpen(false);
    runCvSuggest(parsedId);
  }

  // Wizard-prefill: kör CV-suggest en gång vid montering. "use client"-effekt
  // motiverad — den läser CV:t (server-action) först efter att klient-ön är
  // hydrerad; inget av det går i en Server Component. Körs EN gång (ref-vakt).
  const autoRan = useRef(false);
  useEffect(() => {
    if (autoSuggestFromCv && !autoRan.current) {
      autoRan.current = true;
      runCvSuggest();
    }
    // En-gångs-körning vid montering via ref-vakt; runCvSuggest behöver inte
    // vara i deps (ej refererad reaktivt — ref-vakten gör körningen idempotent).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoSuggestFromCv]);

  const filteredOccupations = useMemo(
    () => filterOptions(occupationOptions, occupationFilter),
    [occupationOptions, occupationFilter]
  );
  const isFiltering = occupationFilter.trim().length > 0;
  const occupationChips = labelsForSelected(selected, occupationOptions);
  const activeGroups =
    occupationFields.find((f) => f.conceptId === activeField)?.occupationGroups ??
    [];

  // "Välj alla yrkesgrupper" för det aktiva yrkesområdet (paritet med jobbsidans
  // JobbFilterPopover "Välj alla X"-rad). Togglar HELA det aktiva fältets grupper
  // i ETT klick men bevarar val i andra fält (merge/diff via onReplace, som
  // skriver flera id på en gång). Tri-state: allt valt → checked, delvis →
  // indeterminate ("mixed").
  const activeFieldGroupIds = activeGroups.map((g) => g.conceptId);
  const allActiveSelected =
    activeFieldGroupIds.length > 0 &&
    activeFieldGroupIds.every((id) => selected.includes(id));
  const someActiveSelected = activeFieldGroupIds.some((id) =>
    selected.includes(id)
  );

  function toggleAllActiveGroups() {
    if (allActiveSelected) {
      onReplace(selected.filter((id) => !activeFieldGroupIds.includes(id)));
    } else {
      onReplace([...new Set([...selected, ...activeFieldGroupIds])]);
    }
  }

  // Stabilt panel-id (aria-controls). useId ger ett hydration-säkert unikt id.
  const reactId = useId();
  const panelId = `${idPrefix}-occ-picker-${reactId}`;
  const filterHelpId = `${idPrefix}-occ-filter-help`;

  function openPicker() {
    setPickerOpen(true);
    // Flytta fokus in i panelen efter att den committats (WCAG 2.4.3 — fokus
    // följer den nyöppnade ytan). queueMicrotask kör efter React-commit.
    queueMicrotask(() => {
      panelRef.current
        ?.querySelector<HTMLElement>("input, [role='option']")
        ?.focus();
    });
  }

  return (
    <>
      {/* Sektionshuvud: rubrik (dialogen) eller bara Rensa-länken (wizarden,
          där DialogTitle bär "Yrken"). Behåll alltid Rensa när något är valt. */}
      {showHeading ? (
        <div className="jp-matchdialog__sectionhead">
          <span id={headingId} className="jp-popover__title">
            {t("matchPrefs.facetOccupations")}
          </span>
          {selected.length > 0 && (
            <button type="button" className="jp-clearlink" onClick={onClear}>
              {t("matchPrefs.clear")}
            </button>
          )}
        </div>
      ) : (
        selected.length > 0 && (
          <div className="jp-matchdialog__sectionhead jp-matchdialog__sectionhead--clearonly">
            <button type="button" className="jp-clearlink" onClick={onClear}>
              {t("matchPrefs.clear")}
            </button>
          </div>
        )
      )}

      <PinnedChips
        items={occupationChips}
        onRemove={onToggle}
        ariaLabel={t("matchPrefs.selectedOccupations")}
      />

      {/* CV-förslagets honest states (pending/noCv/noRole/error/unauthorized).
          "candidates" renderas INTE här (de blev chips ovan via pre-add). I
          dialogen finns en knapp som triggar; i wizarden kör autoSuggest. */}
      <CvSuggestStatus
        result={cvResult}
        pending={isCvSuggesting}
        importCvHref={importCvHref}
        showTrigger={!autoSuggestFromCv}
        onTrigger={() => runCvSuggest()}
        uploadOpen={uploadOpen}
        onOpenUpload={() => setUploadOpen(true)}
        onCancelUpload={() => setUploadOpen(false)}
        onUploaded={handleCvUploaded}
      />

      {/* Manuell tillägg: EN tydlig CTA → inline-disclosure (kollapsad default). */}
      <div className="jp-occpicker">
        <button
          type="button"
          className="jp-occpicker__cta"
          aria-expanded={pickerOpen}
          aria-controls={panelId}
          onClick={() => (pickerOpen ? setPickerOpen(false) : openPicker())}
        >
          <Plus size={16} aria-hidden="true" />
          {t("matchPrefs.occupation.addOccupations")}
        </button>

        {pickerOpen && (
          <div
            id={panelId}
            ref={panelRef}
            className="jp-occpicker__panel"
            role="group"
            aria-label={t("matchPrefs.occupation.addOccupations")}
          >
            <div className="flex flex-col gap-1.5 mb-2">
              <Label htmlFor={`${idPrefix}-occ-filter`}>
                {t("matchPrefs.occupation.filterLabel")}
              </Label>
              <Input
                id={`${idPrefix}-occ-filter`}
                type="text"
                value={occupationFilter}
                onChange={(e) => setOccupationFilter(e.target.value)}
                maxLength={80}
                aria-describedby={filterHelpId}
              />
              <p id={filterHelpId} className="text-body-sm text-text-secondary">
                {t("matchPrefs.occupation.filterHint")}
              </p>
            </div>

            {isFiltering ? (
              <div className="jp-matchdialog__list">
                {filteredOccupations.length === 0 ? (
                  <p className="text-body-sm text-text-secondary px-4 py-3">
                    {t("matchPrefs.occupation.noMatch")}
                  </p>
                ) : (
                  filteredOccupations.map((o) => (
                    <CheckItem
                      key={o.conceptId}
                      label={o.label}
                      checked={selected.includes(o.conceptId)}
                      onToggle={() => onToggle(o.conceptId)}
                    />
                  ))
                )}
              </div>
            ) : (
              <div className="jp-matchdialog__cascade">
                {/* Vänsterkolumnen NAVIGERAR vilket yrkesområde som är aktivt
                    (avslöjar dess yrkesgrupper till höger) — den väljer inget
                    värde (det gör grupp-checkboxarna). Därför en knapp-grupp
                    (role="group" + <button>), inte role="listbox" (en listbox
                    lovar single-tab-stop + roving tabindex + piltangenter,
                    vilket interaktionen aldrig hade). Native <button> ger
                    Enter/Space + fokus gratis; aktiv rad via aria-pressed.
                    Paritet med ort-kaskaden + jobb-popovern (CTO-verdikt
                    2026-06-22). */}
                <div
                  className="jp-matchdialog__cascade-col"
                  role="group"
                  aria-label={t("matchPrefs.occupation.occupationField")}
                >
                  <div className="jp-matchdialog__cascade-colhead">
                    <span className="jp-popover__title">
                      {t("matchPrefs.occupation.occupationField")}
                    </span>
                  </div>
                  {occupationFields.length === 0 ? (
                    <p className="text-body-sm text-text-secondary px-4 py-3">
                      {t("matchPrefs.occupation.fieldsUnavailable")}
                    </p>
                  ) : (
                    occupationFields.map((f) => {
                      const active = f.conceptId === activeField;
                      const hasSel = f.occupationGroups.some((g) =>
                        selected.includes(g.conceptId)
                      );
                      return (
                        <button
                          key={f.conceptId}
                          type="button"
                          className="jp-popover-row"
                          aria-pressed={active}
                          onClick={() => setActiveField(f.conceptId)}
                        >
                          <span className="flex items-center gap-2">
                            {hasSel && !active && (
                              <span
                                aria-hidden="true"
                                className="inline-block size-2 rounded-full bg-(--jp-accent-700)"
                              />
                            )}
                            {f.label}
                          </span>
                          <ChevronRight
                            size={14}
                            className="jp-popover-row__chev"
                            aria-hidden="true"
                          />
                        </button>
                      );
                    })
                  )}
                </div>
                <div
                  className="jp-matchdialog__cascade-col"
                  aria-label={t("matchPrefs.occupation.occupationGroups")}
                >
                  <div className="jp-matchdialog__cascade-colhead">
                    <span className="jp-popover__title">
                      {t("matchPrefs.occupation.occupationGroups")}
                    </span>
                  </div>
                  {activeField === null ? (
                    <p className="text-body-sm text-text-secondary px-4 py-3">
                      {t("matchPrefs.occupation.chooseField")}
                    </p>
                  ) : (
                    <>
                      {activeGroups.length > 0 && (
                        <CheckItem
                          label={t("matchPrefs.occupation.selectAllGroups")}
                          checked={allActiveSelected}
                          indeterminate={someActiveSelected && !allActiveSelected}
                          isAll
                          onToggle={toggleAllActiveGroups}
                        />
                      )}
                      {activeGroups.map((g) => (
                        <CheckItem
                          key={g.conceptId}
                          label={g.label}
                          checked={selected.includes(g.conceptId)}
                          onToggle={() => onToggle(g.conceptId)}
                        />
                      ))}
                    </>
                  )}
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </>
  );
}

/**
 * CV-förslagets honest states (pending/noCv/noRole/error/unauthorized).
 * "candidates" hanteras INTE här — kandidaterna pre-addas till draften som
 * chips av föräldern. Deterministisk läsning — copy säger ALDRIG "AI".
 *
 * Dialogen visar en trigger-knapp ("Föreslå utifrån mitt CV"); wizarden kör
 * autoSuggest (ingen knapp) → `showTrigger` styr om knappen renderas.
 */
function CvSuggestStatus({
  result,
  pending,
  importCvHref,
  showTrigger,
  onTrigger,
  uploadOpen,
  onOpenUpload,
  onCancelUpload,
  onUploaded,
}: {
  readonly result: CvSuggestResult | null;
  readonly pending: boolean;
  readonly importCvHref: string;
  readonly showTrigger: boolean;
  readonly onTrigger: () => void;
  readonly uploadOpen: boolean;
  readonly onOpenUpload: () => void;
  readonly onCancelUpload: () => void;
  readonly onUploaded: (parsedResumeId: string) => void;
}) {
  const t = useTranslations("settings");
  // Fokus följer den nyöppnade upload-ytan (WCAG 2.4.3) — speglar "Lägg till
  // yrken"-disclosurens fokus-flytt. queueMicrotask kör efter React-commit så
  // filinputen är monterad.
  const uploadGroupRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    if (uploadOpen) {
      queueMicrotask(() =>
        uploadGroupRef.current
          ?.querySelector<HTMLElement>("input, button, [tabindex]")
          ?.focus()
      );
    }
  }, [uploadOpen]);

  return (
    <div className="jp-matchdialog__suggest">
      {showTrigger && !uploadOpen && (
        <Button
          type="button"
          variant="secondary"
          disabled={pending}
          onClick={onTrigger}
        >
          {pending
            ? t("matchPrefs.occupation.suggesting")
            : t("matchPrefs.occupation.suggestFromCv")}
        </Button>
      )}

      {pending && !showTrigger && (
        <p
          role="status"
          aria-live="polite"
          className="text-body-sm text-text-secondary"
        >
          {t("matchPrefs.occupation.suggesting")}
        </p>
      )}

      {/* Inline CV-uppladdning (Spår 4): ersätter sid-navigeringen till
          /cv/importera. CvUploadForm är ett vanligt formulär (ingen nästlad
          Radix-Dialog → ingen orphan-overlay) — samma inline-användning som
          welcome-modalen. Importsidan finns kvar som sekundär utväg. */}
      {uploadOpen ? (
        <div
          ref={uploadGroupRef}
          className="jp-matchdialog__cvupload"
          role="group"
          aria-label={t("matchPrefs.occupation.uploadGroup")}
        >
          <CvUploadForm onUploaded={onUploaded} />
          <div className="jp-matchdialog__cvupload-foot">
            {/* Neutral avbryt (backar bara ut — INGEN destruktiv handling, så
                aldrig .jp-clearlink/röd, WCAG 1.4.1). Ghost-knapp = neutral och
                tydligt en knapp, skild från navigations-länken bredvid. */}
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={onCancelUpload}
            >
              {t("matchPrefs.occupation.cancelUpload")}
            </Button>
            <a
              className="text-body-sm text-text-secondary underline underline-offset-2"
              href={importCvHref}
            >
              {t("matchPrefs.occupation.openImportInstead")}
            </a>
          </div>
        </div>
      ) : (
        result !== null && (
          <CvSuggestMessage result={result} onOpenUpload={onOpenUpload} />
        )
      )}
    </div>
  );
}

/** De fyra honest non-candidate-states. "candidates" → null (blev chips). */
function CvSuggestMessage({
  result,
  onOpenUpload,
}: {
  readonly result: CvSuggestResult;
  readonly onOpenUpload: () => void;
}) {
  const t = useTranslations("settings");
  switch (result.kind) {
    case "candidates":
      // Pre-addade som chips av föräldern — ingen separat checklista.
      return null;
    case "noCv":
      return (
        <div
          role="status"
          className="rounded-md border border-border-default bg-surface-secondary p-3"
        >
          <p className="text-body-sm text-text-primary font-medium">
            {t("matchPrefs.occupation.noCvTitle")}
          </p>
          <p className="text-body-sm text-text-secondary mt-1">
            {t("matchPrefs.occupation.noCvBody")}
          </p>
          {/* Spår 4: laddar upp inline i modalen i stället för att navigera bort. */}
          <Button
            type="button"
            variant="secondary"
            className="mt-2.5"
            onClick={onOpenUpload}
          >
            {t("matchPrefs.occupation.uploadCv")}
          </Button>
        </div>
      );
    case "noRole":
      return (
        <p role="status" className="text-body-sm text-text-secondary">
          {t("matchPrefs.occupation.noRole")}
        </p>
      );
    case "unauthorized":
      return (
        <p role="alert" className="text-body-sm text-danger-600">
          {t("matchPrefs.occupation.unauthorized")}
        </p>
      );
    case "error":
      return (
        <p role="alert" className="text-body-sm text-danger-600">
          {t("matchPrefs.occupation.error")}
        </p>
      );
  }
}
