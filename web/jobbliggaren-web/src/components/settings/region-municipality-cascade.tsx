"use client";

// "use client": ort-sektionen håller en disclosure-toggle ("Lägg till orter"),
// en aktiv-län-kolumn-state och en label-filter-state. Den delas av
// match-setup-rail-modal OCH match-preferences-dialog (DRY, Spår 3 PR-D) — samma
// roll som OccupationSection fyller för yrke. Inget av detta går i en Server
// Component.
//
// Spår 3 PR-D (ADR 0076-amendment 2026-06-21): ort är EN dimension i två
// granulariteter. Vi ÅTERANVÄNDER jobbsidans dual-axis-logik (ort-selection.ts:
// toggleWholeRegion / toggleMunicipalityInRegion / clearRegionColumn) i stället
// för att forka den — samma "Hela länet"-semantik som hero-popovern, bara
// presenterad inline (ingen positionerad popover: en absolut-positionerad
// JobbFilterPopover misspositioneras + slåss med Radix Dialogens fokus-trap
// inuti modalen, exakt som OccupationSection redan motiverar för yrke).

import { useId, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { ChevronRight, Plus } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { TaxonomyRegion } from "@/lib/dto/taxonomy";
import {
  toggleWholeRegion,
  toggleMunicipalityInRegion,
  clearRegionColumn,
  type OrtSelection,
} from "@/lib/job-ads/ort-selection";
import { CheckItem, PinnedChips } from "./section-helpers";
import { filterOptions, labelsForSelected, type Option } from "./match-preferences-shared";

interface RegionMunicipalityCascadeProps {
  /** Län (med underordnade kommuner) ur taxonomin. */
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  /** Valda län-concept-id (helläns-axeln, draft). */
  readonly selectedRegions: ReadonlyArray<string>;
  /** Valda kommun-concept-id (kommun-axeln, draft). */
  readonly selectedMunicipalities: ReadonlyArray<string>;
  /**
   * Commit av HELA ort-paret (region + kommun) i ett anrop. Föräldern äger
   * draft-state och submittar båda atomiskt (NOTE-1) — därför EN callback med
   * båda axlarna, aldrig två separata setters som kan glida isär.
   */
  readonly onChange: (next: OrtSelection) => void;
  /**
   * Visa sektionens egna "Orter"-rubrik. Default true (dialogen). Wizarden
   * sätter false: där bär steg-rubriken ("Orter") rubriken, och en andra
   * inline-rubrik vore en dubblett. När false renderas bara Rensa-länken.
   */
  readonly showHeading?: boolean;
  /** rubrik-id som värden kopplar `aria-labelledby` mot (för role=group). */
  readonly headingId?: string;
  /** Unik DOM-id-prefix så sektionen kan monteras i flera värdar (dialog/wizard). */
  readonly idPrefix?: string;
}

/**
 * ORT-sektionen: pinnade chips (valda län + kommuner) + EN "Lägg till orter"-CTA
 * som veck-öppnar en tvåkolumns Län→Kommuner-kaskad med en "Hela länet"-rad
 * (samma UX som jobbsidans Ort-popover). Återanvänds av BÅDE
 * match-preferences-dialog och match-setup-rail-modal.
 *
 * "Hela länet" = länets concept-id i `region` (matchar backend region-träff);
 * enskild kommun = kommun-concept-id i `municipality` (kommun-träff). Backend
 * unionerar region ∪ municipality — normaliseringen (ort-selection.ts) håller
 * valet minimalt och chipsen begripliga, men bär ingen korrekthet (ren UX).
 */
export function RegionMunicipalityCascade({
  regions,
  selectedRegions,
  selectedMunicipalities,
  onChange,
  showHeading = true,
  headingId,
  idPrefix = "match-dialog",
}: RegionMunicipalityCascadeProps) {
  const t = useTranslations("settings");
  const [pickerOpen, setPickerOpen] = useState(false);
  const [activeRegion, setActiveRegion] = useState<string | null>(null);
  const [filter, setFilter] = useState("");

  const reactId = useId();
  const panelId = `${idPrefix}-ort-picker-${reactId}`;
  const filterId = `${idPrefix}-ort-filter`;
  const filterHelpId = `${idPrefix}-ort-filter-help`;

  // Lookups för dual-axis-normaliseringen (ort-selection.ts). Speglar
  // jobb-hero-filters.tsx: kommun→län-förälder + länets kommun-id-lista.
  const regionOfMunicipality = useMemo(() => {
    const map = new Map<string, string>();
    for (const r of regions)
      for (const m of r.municipalities) map.set(m.conceptId, r.conceptId);
    return map;
  }, [regions]);
  const municipalityIdsOfRegion = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const r of regions)
      map.set(
        r.conceptId,
        r.municipalities.map((m) => m.conceptId),
      );
    return map;
  }, [regions]);

  const current: OrtSelection = {
    region: selectedRegions,
    municipality: selectedMunicipalities,
  };
  const hasAnySelected =
    selectedRegions.length > 0 || selectedMunicipalities.length > 0;

  // Pinnade chips: valda län FÖRST (helläns-axeln), sedan enskilda kommuner.
  // Båda visar svenska namn ur taxonomin; okänt id → id-strängen (aldrig tom).
  const regionLabelOptions = useMemo<ReadonlyArray<Option>>(
    () => regions.map((r) => ({ conceptId: r.conceptId, label: r.label })),
    [regions],
  );
  const municipalityLabelOptions = useMemo<ReadonlyArray<Option>>(
    () =>
      regions.flatMap((r) =>
        r.municipalities.map((m) => ({ conceptId: m.conceptId, label: m.label })),
      ),
    [regions],
  );
  const regionChips = labelsForSelected(selectedRegions, regionLabelOptions);
  const municipalityChips = labelsForSelected(
    selectedMunicipalities,
    municipalityLabelOptions,
  );
  const pinnedChips: ReadonlyArray<Option> = [
    ...regionChips,
    ...municipalityChips,
  ];

  // Label-filter: smalnar av kommun-listan tvärs över ALLA län (Platsbanken-
  // paritet med yrkes-sektionens filter). Tomt filter → kaskad-vyn.
  const allMunicipalityOptions = municipalityLabelOptions;
  const filteredMunicipalities = useMemo(
    () => filterOptions(allMunicipalityOptions, filter),
    [allMunicipalityOptions, filter],
  );
  const isFiltering = filter.trim().length > 0;

  const activeRegionData =
    regions.find((r) => r.conceptId === activeRegion) ?? null;
  const activeMunicipalities = activeRegionData?.municipalities ?? [];
  const activeWholeSelected =
    activeRegionData != null && selectedRegions.includes(activeRegionData.conceptId);

  function openPicker() {
    setPickerOpen(true);
  }

  /** En chip (län eller kommun) togglas av via kryss-ikonen. */
  function removeChip(conceptId: string) {
    if (selectedRegions.includes(conceptId)) {
      // En läns-chip: rensa hela länets kolumn (region-id + ev. kommun-rester).
      onChange(
        clearRegionColumn(
          current,
          conceptId,
          municipalityIdsOfRegion.get(conceptId) ?? [],
        ),
      );
      return;
    }
    // En kommun-chip: toggla av kommunen via per-län-semantiken.
    const parent = regionOfMunicipality.get(conceptId);
    onChange(
      toggleMunicipalityInRegion(
        current,
        conceptId,
        parent ?? "",
        parent ? (municipalityIdsOfRegion.get(parent) ?? []) : [],
      ),
    );
  }

  function toggleRegion(regionConceptId: string) {
    onChange(
      toggleWholeRegion(
        current,
        regionConceptId,
        municipalityIdsOfRegion.get(regionConceptId) ?? [],
      ),
    );
  }
  function toggleMunicipality(
    municipalityConceptId: string,
    regionConceptId: string,
  ) {
    onChange(
      toggleMunicipalityInRegion(
        current,
        municipalityConceptId,
        regionConceptId,
        municipalityIdsOfRegion.get(regionConceptId) ?? [],
      ),
    );
  }

  return (
    <>
      {showHeading ? (
        <div className="jp-matchdialog__sectionhead">
          <span id={headingId} className="jp-popover__title">
            {t("matchPrefs.cascade.heading")}
          </span>
          {hasAnySelected && (
            <button
              type="button"
              className="jp-clearlink"
              onClick={() => onChange({ region: [], municipality: [] })}
            >
              {t("matchPrefs.cascade.clear")}
            </button>
          )}
        </div>
      ) : (
        hasAnySelected && (
          <div className="jp-matchdialog__sectionhead jp-matchdialog__sectionhead--clearonly">
            <button
              type="button"
              className="jp-clearlink"
              onClick={() => onChange({ region: [], municipality: [] })}
            >
              {t("matchPrefs.cascade.clear")}
            </button>
          </div>
        )
      )}

      <PinnedChips
        items={pinnedChips}
        onRemove={removeChip}
        ariaLabel={t("matchPrefs.cascade.selectedAria")}
      />

      <div className="jp-occpicker">
        <button
          type="button"
          className="jp-occpicker__cta"
          aria-expanded={pickerOpen}
          aria-controls={panelId}
          onClick={() => (pickerOpen ? setPickerOpen(false) : openPicker())}
        >
          <Plus size={16} aria-hidden="true" />
          {t("matchPrefs.cascade.addOrter")}
        </button>

        {pickerOpen && (
          <div
            id={panelId}
            className="jp-occpicker__panel"
            role="group"
            aria-label={t("matchPrefs.cascade.addOrter")}
          >
            <div className="flex flex-col gap-1.5 mb-2">
              <Label htmlFor={filterId}>
                {t("matchPrefs.cascade.filterLabel")}
              </Label>
              <Input
                id={filterId}
                type="text"
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
                maxLength={80}
                aria-describedby={filterHelpId}
              />
              <p id={filterHelpId} className="text-body-sm text-text-secondary">
                {t("matchPrefs.cascade.filterHint")}
              </p>
            </div>

            {isFiltering ? (
              <div className="jp-matchdialog__list">
                {filteredMunicipalities.length === 0 ? (
                  <p className="text-body-sm text-text-secondary px-4 py-3">
                    {t("matchPrefs.cascade.noMatch")}
                  </p>
                ) : (
                  filteredMunicipalities.map((m) => {
                    const parent = regionOfMunicipality.get(m.conceptId);
                    const wholeParentSelected =
                      parent != null && selectedRegions.includes(parent);
                    return (
                      <CheckItem
                        key={m.conceptId}
                        label={m.label}
                        checked={
                          selectedMunicipalities.includes(m.conceptId) ||
                          wholeParentSelected
                        }
                        onToggle={() =>
                          parent != null &&
                          toggleMunicipality(m.conceptId, parent)
                        }
                      />
                    );
                  })
                )}
              </div>
            ) : (
              <div className="jp-matchdialog__cascade">
                {/* Vänsterkolumnen NAVIGERAR vilket län som är aktivt (avslöjar
                    dess kommuner till höger) — den väljer inget värde (det gör
                    kommun-checkboxarna + de pinnade chipsen). Därför en
                    knapp-grupp (role="group" + <button>), inte role="listbox"
                    (en listbox lovar single-tab-stop + roving tabindex +
                    piltangenter, vilket interaktionen aldrig hade). Native
                    <button> ger Enter/Space + fokus gratis; aktiv rad via
                    aria-pressed. Paritet med jobb-popovern (CTO-verdikt
                    2026-06-22). */}
                <div
                  className="jp-matchdialog__cascade-col"
                  role="group"
                  aria-label={t("matchPrefs.cascade.regionColumn")}
                >
                  <div className="jp-matchdialog__cascade-colhead">
                    <span className="jp-popover__title">
                      {t("matchPrefs.cascade.regionColumn")}
                    </span>
                  </div>
                  {regions.length === 0 ? (
                    <p className="text-body-sm text-text-secondary px-4 py-3">
                      {t("matchPrefs.cascade.regionsUnavailable")}
                    </p>
                  ) : (
                    regions.map((r) => {
                      const active = r.conceptId === activeRegion;
                      const hasSel =
                        selectedRegions.includes(r.conceptId) ||
                        r.municipalities.some((m) =>
                          selectedMunicipalities.includes(m.conceptId),
                        );
                      return (
                        <button
                          key={r.conceptId}
                          type="button"
                          className="jp-popover-row"
                          aria-pressed={active}
                          onClick={() => setActiveRegion(r.conceptId)}
                        >
                          <span className="flex items-center gap-2">
                            {hasSel && !active && (
                              <span
                                aria-hidden="true"
                                className="inline-block size-2 rounded-full bg-(--jp-accent-700)"
                              />
                            )}
                            {r.label}
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
                  aria-label={t("matchPrefs.cascade.municipalityColumn")}
                >
                  <div className="jp-matchdialog__cascade-colhead">
                    <span className="jp-popover__title">
                      {t("matchPrefs.cascade.municipalityColumn")}
                    </span>
                  </div>
                  {activeRegionData === null ? (
                    <p className="text-body-sm text-text-secondary px-4 py-3">
                      {t("matchPrefs.cascade.chooseRegion")}
                    </p>
                  ) : activeMunicipalities.length === 0 ? (
                    <p className="text-body-sm text-text-secondary px-4 py-3">
                      {t("matchPrefs.cascade.municipalitiesUnavailable")}
                    </p>
                  ) : (
                    <>
                      {/* "Hela länet"-raden togglar länets concept-id i region-
                          axeln (ETT id, aldrig materialiserade kommun-ids).
                          Tri-state: hela länet → checked; några kommuner valda →
                          indeterminate ("mixed"). Paritet med jobbsidans popover. */}
                      <CheckItem
                        label={t("matchPrefs.cascade.wholeRegion", {
                          label: activeRegionData.label,
                        })}
                        checked={activeWholeSelected}
                        indeterminate={
                          !activeWholeSelected &&
                          activeMunicipalities.some((m) =>
                            selectedMunicipalities.includes(m.conceptId),
                          )
                        }
                        isAll
                        onToggle={() => toggleRegion(activeRegionData.conceptId)}
                      />
                      {activeMunicipalities.map((m) => (
                        <CheckItem
                          key={m.conceptId}
                          label={m.label}
                          // När hela länet är valt renderas kommun-raderna
                          // markerade (Platsbanken-paritet — tydligt vad valet
                          // omfattar); klick = "hela länet minus denna kommun".
                          checked={
                            selectedMunicipalities.includes(m.conceptId) ||
                            activeWholeSelected
                          }
                          onToggle={() =>
                            toggleMunicipality(
                              m.conceptId,
                              activeRegionData.conceptId,
                            )
                          }
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
