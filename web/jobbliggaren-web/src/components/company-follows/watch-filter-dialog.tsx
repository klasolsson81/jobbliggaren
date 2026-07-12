"use client";

// "use client": dialogen håller DRAFT-state (ort-paret + "endast matchande") och en useTransition runt
// save-actionen. Draften committas atomiskt med "Spara filter" — inget av detta går i en Server Component.

import { useId, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import Link from "next/link";
import {
  Dialog,
  DialogContent,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { CheckItem } from "@/components/settings/section-helpers";
import { RegionMunicipalityCascade } from "@/components/settings/region-municipality-cascade";
import { InfoDialog } from "@/components/common/info-dialog";
import type { TaxonomyRegion } from "@/lib/dto/taxonomy";
import type { OrtSelection } from "@/lib/job-ads/ort-selection";
import type { WatchFilter } from "@/lib/dto/company-follows";
import { setWatchFilterAction } from "@/lib/actions/company-follows";

// Samma nudge-mål som radens "Ställ in matchning" (SPOT — en väg till matchnings-inställningarna).
const MATCH_SETTINGS_HREF = "/installningar#matchning";

interface WatchFilterDialogProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly companyWatchId: string;
  readonly companyName: string;
  /** Det PERSISTERADE filtret (SSOT) — draften seedas från det vid öppning. `null` = inget filter. */
  readonly filter: WatchFilter | null;
  /** Länen (med kommuner) ur taxonomin — samma träd ort-pickern använder överallt annars. */
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  /**
   * `true` när användaren inte angett något yrke. Filtret får ändå SÄTTAS (backend accepterar det och
   * håller det INERT) — vi renderar en ärlig nudge i stället för att låsa kontrollen. Se doc:en nedan.
   */
  readonly matchingNotAssessed: boolean;
}

/**
 * Bevakning F4b (#803) — per-bevakningsfilter för notiser.
 *
 * <b>Dialog, inte inline-utfällning</b> (design-bind 2026-07-12): ort-kaskaden är stor, raden ska
 * förbli skannbar, och dialogen ger en tydlig commit-gräns (draft → "Spara filter") som matchar
 * filtrets atomära full-replace-semantik. Kaskaden är redan bevisad i två dialog-värdar.
 *
 * <b>Filtret gäller NOTISERNA, inte listans siffror.</b> Radens "aktiva annonser" / "matchande annonser"
 * är medvetet INTE filter-medvetna (RF-8): de svarar på "postar det här företaget annonser jag matchar?"
 * (ett följ-BESLUT), medan filtret svarar på "vilka av dem ska notifiera mig". Copy:n säger det.
 *
 * <b>"Endast matchande" låses ALDRIG</b> — inte ens när användaren saknar matchningsprofil (CTO Q8-b).
 * Backend accepterar värdet och håller filtret INERT tills ett yrke anges, och 8C:s read-time-gradering
 * är vald just för att filtret ska börja gälla i samma stund profilen finns, utan att användaren måste
 * komma tillbaka hit. En låst kontroll gör det omöjligt att uttrycka, och skulle dessutom vara en regel
 * FE:n hittat på som backend inte har. I stället: sätt det fritt, och säg sanningen bredvid.
 *
 * <b>Ett tomt val RENSAR filtret</b> — det är den naturligaste handlingen för "jag vill inte filtrera
 * längre", och backend mappar det till den kanoniska NULL:en. Det får aldrig bli ett valideringsfel.
 */
export function WatchFilterDialog({
  open,
  onOpenChange,
  companyWatchId,
  companyName,
  filter,
  regions,
  matchingNotAssessed,
}: WatchFilterDialogProps) {
  const t = useTranslations("jobads.companyWatches.filter");
  const inertNudgeId = useId();

  // Draften seedas från det persisterade filtret. `key` på dialogen (i raden) monterar om komponenten
  // när filtret ändras, så draften kan aldrig visa ett inaktuellt värde efter en revalidate.
  const [draftRegions, setDraftRegions] = useState<ReadonlyArray<string>>(
    filter?.regions ?? []
  );
  const [draftMunicipalities, setDraftMunicipalities] = useState<
    ReadonlyArray<string>
  >(filter?.municipalities ?? []);
  const [draftOnlyMatched, setDraftOnlyMatched] = useState<boolean>(
    filter?.onlyMatched ?? false
  );
  const [isSaving, startSaving] = useTransition();
  const [saveError, setSaveError] = useState<string | null>(null);

  function onOrtChange(next: OrtSelection) {
    setDraftRegions(next.region);
    setDraftMunicipalities(next.municipality);
  }

  function clearDraft() {
    setDraftRegions([]);
    setDraftMunicipalities([]);
    setDraftOnlyMatched(false);
  }

  const draftHasFilter =
    draftRegions.length > 0 ||
    draftMunicipalities.length > 0 ||
    draftOnlyMatched;

  function handleSave() {
    setSaveError(null);
    startSaving(async () => {
      const result = await setWatchFilterAction(companyWatchId, {
        // De två axlarna hålls isär hela vägen: ett helt-läns-val skickas som ett LÄNS-id, aldrig
        // expanderat till länets kommuner (en läns-taggad annons bär ingen kommun och skulle då tyst
        // falla bort ur notiserna).
        municipalities: draftMunicipalities,
        regions: draftRegions,
        onlyMatched: draftOnlyMatched,
      });

      if (!result.success) {
        // Felet bor i dialogen, som stannar öppen — ingen revalidate, ingen förlorad draft.
        setSaveError(result.error);
        return;
      }

      // Stäng FÖRE revalidaten landar: en Server Action som re-renderar RSC-trädet avmonterar en öppen
      // dialog mitt i flödet (#141). Fokus återgår till "Filtrera"-knappen (Radix).
      onOpenChange(false);
    });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="jp-matchdialog">
        {/* `__head` bär den padding-right som håller titeln fri från den absolut-positionerade
            ×-knappen — ett långt företagsnamn skulle annars löpa in under den. */}
        <div className="jp-matchdialog__head">
          <DialogTitle className="jp-matchdialog__title">
            {t("dialogTitle", { company: companyName })}
          </DialogTitle>
          <DialogDescription className="jp-matchdialog__intro">
            {t("dialogIntro")}
          </DialogDescription>
        </div>

        <div className="jp-matchdialog__body">
          <section className="jp-matchdialog__section">
            <div className="jp-settings-field">
              <div className="flex items-center gap-3">
                <CheckItem
                  label={t("onlyMatchedLabel")}
                  checked={draftOnlyMatched}
                  onToggle={() => setDraftOnlyMatched((prev) => !prev)}
                  // Kontrollen låses ALDRIG (se doc:en ovan) — men då MÅSTE skälet till att filtret är
                  // inert nå en skärmläsar-användare via kontrollen själv. I forms-mode läses bara det
                  // tillgängliga namnet + beskrivningen: utan den här kopplingen hörs "kryssruta, ej
                  // markerad", användaren kryssar i, sparar, och får ett inert filter utan att någonsin
                  // få veta varför. Det vore den tysta smalningen igen, ett lager ned.
                  describedBy={matchingNotAssessed ? inertNudgeId : undefined}
                />
                {/* InfoDialog som SYSKON till kontrollraden — aldrig som barn (ett klick på "?" får
                    inte toggla kontrollen). */}
                <InfoDialog
                  title={t("onlyMatchedHelpTitle")}
                  paragraphs={[t("onlyMatchedHelpBody1"), t("onlyMatchedHelpBody2")]}
                  ariaLabel={t("onlyMatchedHelpAria")}
                />
              </div>
              <p className="jp-settings-field__hint">{t("onlyMatchedHelp")}</p>
              {matchingNotAssessed && (
                // Ärligt not-assessed: filtret SPARAS men gäller inte förrän ett yrke angetts. Vi
                // låser inte kontrollen — filtret aktiveras retroaktivt i samma stund profilen finns.
                <p id={inertNudgeId} className="jp-matchline">
                  {t("onlyMatchedInert")}{" "}
                  <Link href={MATCH_SETTINGS_HREF} className="jp-nudgelink">
                    {t("onlyMatchedInertCta")}
                  </Link>
                </p>
              )}
            </div>
          </section>

          <section
            className="jp-matchdialog__section"
            role="group"
            aria-labelledby="watch-filter-ort-head"
          >
            <h3 id="watch-filter-ort-head" className="jp-settings-field__label">
              {t("ortHeading")}
            </h3>
            <p className="jp-settings-field__hint">{t("ortHelp")}</p>
            <RegionMunicipalityCascade
              regions={regions}
              selectedRegions={draftRegions}
              selectedMunicipalities={draftMunicipalities}
              onChange={onOrtChange}
              showHeading={false}
              idPrefix="watch-filter-ort"
            />
          </section>
        </div>

        {/* Husets modal-fot (paritet match-preferences-dialog): primär först, sedan avbryt, sedan
            felet. Den bär padding, gap och den nedre radien — utan den ligger knapparna kant i kant
            mot modalens hörn. */}
        <div className="jp-matchdialog__foot">
          <Button type="button" onClick={handleSave} disabled={isSaving}>
            {isSaving ? t("saving") : t("save")}
          </Button>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={isSaving}
          >
            {t("cancel")}
          </Button>
          {/* Visas bara när det FINNS något att rensa. Rensar DRAFTEN — "Spara filter" är den enda
              commit-gränsen, så ett oavsiktligt klick är ångrbart med "Avbryt". */}
          {draftHasFilter && (
            <button
              type="button"
              className="jp-clearlink"
              onClick={clearDraft}
              disabled={isSaving}
            >
              {t("clearAll")}
            </button>
          )}
          {saveError && (
            <p role="alert" className="text-body-sm text-danger-600">
              {saveError}
            </p>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
