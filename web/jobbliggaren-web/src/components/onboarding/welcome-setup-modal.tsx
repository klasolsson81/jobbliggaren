"use client";

// "use client": välkomst-modalen håller klient-only state (vilket steg, om
// wizarden är öppen), kopplar en Server Action (markSetupWelcomeSeen) på
// close/skip/Esc i en transition, och flyttar fokus programmatiskt till varje
// stegs rubrik (WCAG 2.4.3). Server-komponenten (/oversikt) avgör `showWelcome`
// och hämtar taxonomi + persisterade preferenser och skickar in dem som
// serialiserbara props. Inget av detta går i en Server Component.

import { useEffect, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { CheckCircle2 } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { BrandSpinner } from "@/components/brand/brand-spinner";
import { CvUploadForm } from "@/components/resumes/cv-upload-form";
import { CvGapFillForm } from "@/components/resumes/cv-gapfill-form";
import { MatchSetupWizard } from "@/components/settings/match-setup-wizard";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { ParsedContentDto } from "@/lib/dto/parsed-resume";
import { loadParsedResumeForGapFillAction } from "@/lib/actions/resumes";
import { markSetupWelcomeSeen } from "@/lib/onboarding/setup-welcome-actions";

/** Welcome-modalens interna steg (klient-only — ingen route per steg).
 * "gapfill" (STEG 1 / ADR 0079): in-modal komplettering + promote av det
 * uppladdade CV:t — utan detta blir CV:t aldrig ett kanoniskt Resume (CV-sidan
 * tom + matchningen ser inget CV). "done" slår ihop bekräftelse + val i EN slide
 * (Klas: separat confirm + choice kändes som dubbelsteg). */
type WelcomeStep = "upload" | "gapfill" | "done";

/** Server-läst parse-artefakt för in-modal-gap-fill (CV-PII, samma exponering
 * som granska-sidan redan gör mot klient-formen). */
type GapFillData = {
  readonly sourceFileName: string;
  readonly content: ParsedContentDto;
};

interface WelcomeSetupModalProps {
  readonly showWelcome: boolean;
  // Wizard-data — samma serialiserbara props som /cv och /installningar matar
  // in i MatchSetupWizard (taxonomi + persisterad SSOT).
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
  /** Spår 3 PR-D: kommun-axeln (pre-fill för wizardens ort-steg). */
  readonly persistedMunicipalities: ReadonlyArray<string>;
  readonly persistedEmploymentTypes: ReadonlyArray<string>;
  /** CV-importflödets route (wizardens yrkes-steg tom-state-länk). */
  readonly importCvHref: string;
}

/**
 * Välkomst-/första-setup-modal (ADR 0077 STEG 5). Triggas av server-signaler
 * (profil utan angett yrke + ingen setup-welcome-cookie) och avfärdas via en
 * `__Host-`-funktionscookie — ingen ny backend-kolumn (ADR 0077 / ADR 0076
 * Decision 3). Flödet (Klas-vision): ladda upp CV → stor bekräftelse med grön
 * check → val "ställa in matchning nu?" → wizarden (welcome step 2, en enda
 * komponent med två ingångar).
 *
 * Skriver inget själv utöver cookien — den enda preferens-skrivningen är
 * wizardens befintliga MatchPreferences-PUT.
 */
export function WelcomeSetupModal({
  showWelcome,
  occupationFields,
  regions,
  employmentTypes,
  persistedOccupationGroups,
  persistedRegions,
  persistedMunicipalities,
  persistedEmploymentTypes,
  importCvHref,
}: WelcomeSetupModalProps) {
  const t = useTranslations("settings");
  const router = useRouter();
  const [welcomeOpen, setWelcomeOpen] = useState(showWelcome);
  const [wizardOpen, setWizardOpen] = useState(false);
  const [step, setStep] = useState<WelcomeStep>("upload");
  // Om ett CV faktiskt laddades upp OCH befordrades (styr grön check + copy i
  // "done"-steget; "Fortsätt utan CV" hoppar direkt till "done" UTAN check).
  const [uploaded, setUploaded] = useState(false);
  // Id för det just uppladdade parsed_resume:t (bärs till gap-fill-steget för promote).
  const [uploadedParsedId, setUploadedParsedId] = useState<string | null>(null);
  // STEG 1 / ADR 0079: server-läst parse-innehåll för in-modal-gap-fill (null tills
  // laddat), de förhämtade multi-signal-yrkesförslagen (#145, bevaras genom promote),
  // och ett ladd-fel-tillstånd.
  const [gapFillData, setGapFillData] = useState<GapFillData | null>(null);
  const [proposedOccupations, setProposedOccupations] = useState<string[]>([]);
  const [gapFillError, setGapFillError] = useState(false);
  const [, startLoadingGapFill] = useTransition();
  const [, startTransition] = useTransition();

  // Fokus till stegrubriken EFTER commit (WCAG 2.4.3 + 4.1.3). En effekt på
  // `step` garanterar att den nya DialogTitle är monterad när `.focus()` körs —
  // så skärmläsaren annonserar den nya rubriken (t.ex. "CV uppladdat" vid
  // lyckad uppladdning). En queueMicrotask i event-handlern kunde köra före
  // React committat den nya titeln (text-mutation på redan fokuserad nod
  // annonseras inte). `welcomeOpen`-guarden hindrar fokus-stöld när wizarden tar
  // över.
  const titleRef = useRef<HTMLHeadingElement | null>(null);
  useEffect(() => {
    if (welcomeOpen) titleRef.current?.focus();
  }, [step, welcomeOpen]);

  // Markera cookien sedd så modalen inte återkommer. Wrappa i transition så
  // UI:t inte väntar på round-trip; router.refresh() läser om RSC-trädet så
  // `showWelcome` blir false vid nästa navigation (mirror guest-mode).
  function persistSeen() {
    startTransition(async () => {
      await markSetupWelcomeSeen();
      router.refresh();
    });
  }

  /** Stäng/skippa/Esc utan att gå vidare till wizarden → markera sedd. */
  function dismissWelcome() {
    setWelcomeOpen(false);
    persistSeen();
  }

  /**
   * Öppna wizarden (welcome step 2). TVÅ fällor, båda verifierade i browser:
   *
   * 1. Sätt INTE cookien här. `markSetupWelcomeSeen` är en Server Action, och
   *    Next re-renderar den aktuella routens RSC efter varje server-action →
   *    /oversikt om-evaluerar `showWelcome=false` (cookien satt) → HELA
   *    WelcomeSetupModal avmonteras innan wizarden hinner öppnas (symptom: 0
   *    dialoger efter "Ja"). Cookien sätts i stället när wizarden STÄNGS.
   * 2. Toggla inte två Radix-dialoger i SAMMA commit. Stäng välkomsten först
   *    (open=false → Radix Presence avmonterar overlayn rent) och öppna wizarden
   *    EFTER en kort fördröjning (>stäng-animationen) — annars lämnas en orphan
   *    aria-hidden-overlay som blockerar wizarden.
   */
  function openWizard() {
    setWelcomeOpen(false);
    window.setTimeout(() => setWizardOpen(true), 320);
  }

  /**
   * Upload klar → gå till gap-fill-steget och läs parse-innehållet server-side.
   * STEG 1 / ADR 0079: utan att CV:t befordras (promotas) blir det aldrig ett
   * kanoniskt Resume → CV-sidan tom + matchningen ser inget CV. De rika
   * multi-signal-yrkesförslagen (#145) förhämtas HÄR, medan staging-artefakten
   * lever, så de överlever den efterföljande promoten (som soft-raderar den).
   */
  function handleCvUploaded(parsedResumeId: string) {
    setUploadedParsedId(parsedResumeId);
    setGapFillError(false);
    setGapFillData(null);
    setProposedOccupations([]);
    setStep("gapfill");
    startLoadingGapFill(async () => {
      const result = await loadParsedResumeForGapFillAction(parsedResumeId);
      if (result.kind === "ok") {
        setGapFillData({
          sourceFileName: result.sourceFileName,
          content: result.content,
        });
        setProposedOccupations(result.proposedOccupationGroups);
      } else {
        setGapFillError(true);
      }
    });
  }

  /** Gap-fill klar (CV befordrat till kanoniskt Resume) → bekräftelse + val. */
  function handlePromoted() {
    setUploaded(true);
    setStep("done");
  }

  return (
    <>
      {/* Aldrig två öppna Radix-dialoger samtidigt. openWizard stänger denna
          FÖRST (open=false, ren Presence-avmontering) och öppnar wizarden efter
          en fördröjning. `!wizardOpen`-gaten är belt-and-suspenders — vid den
          tidpunkten har välkomsten redan stängts klart, så detta avmonterar
          aldrig en öppen dialog (ingen orphan-overlay). */}
      {!wizardOpen && (
        <Dialog
          open={welcomeOpen}
          onOpenChange={(next) => {
            // Endast stängning hanteras här (scrim-klick, Esc, X). Öppning styrs
            // av server-proppen vid mount. openWizard sätter open=false direkt
            // OCH tar dialogen ur trädet (wizardOpen) → triggar inte denna.
            if (!next) dismissWelcome();
          }}
        >
          <DialogContent
            className={`jp-stdmodal ${
              step === "gapfill" ? "jp-stdmodal--wide" : "jp-stdmodal--narrow"
            }`}
          >
            {step === "upload" ? (
              <>
                <div className="jp-welcome__head">
                  <DialogTitle
                    ref={titleRef}
                    tabIndex={-1}
                    className="jp-welcome__title"
                  >
                    {t("onboarding.uploadTitle")}
                  </DialogTitle>
                  <DialogDescription className="jp-welcome__intro">
                    {stepIntro(t, "upload")}
                  </DialogDescription>
                </div>

                <div className="jp-welcome__body">
                  <CvUploadForm onUploaded={handleCvUploaded} />
                  <div className="jp-welcome__skiprow">
                    <button
                      type="button"
                      className="jp-welcome__skip"
                      onClick={() => setStep("done")}
                    >
                      {t("onboarding.continueWithoutCv")}
                    </button>
                  </div>
                </div>
              </>
            ) : step === "gapfill" ? (
              // "gapfill" (STEG 1 / ADR 0079): komplettera + befordra CV:t I
              // modalen. Datum per jobb/utbildning krävs (parsern gissar aldrig,
              // backend syntetiserar aldrig) → den enda ärliga vägen till ett
              // kanoniskt Resume går genom användarens gap-fill. Vid lyckad
              // promote → "done"-steget; CV-sidan + matchningen ser nu CV:t.
              <>
                <div className="jp-welcome__head">
                  <DialogTitle
                    ref={titleRef}
                    tabIndex={-1}
                    className="jp-welcome__title"
                  >
                    {t("onboarding.gapFillTitle")}
                  </DialogTitle>
                  <DialogDescription className="jp-welcome__intro">
                    {t("onboarding.gapFillIntro")}
                  </DialogDescription>
                </div>
                <div className="jp-welcome__body">
                  {gapFillError ? (
                    <div
                      role="alert"
                      className="flex flex-col items-start gap-3"
                    >
                      <p className="text-body-sm text-text-secondary">
                        {t("onboarding.gapFillError")}
                      </p>
                      <Button
                        type="button"
                        variant="ghost"
                        onClick={() => setStep("done")}
                      >
                        {t("onboarding.continueWithoutCv")}
                      </Button>
                    </div>
                  ) : gapFillData !== null && uploadedParsedId !== null ? (
                    <CvGapFillForm
                      parsedId={uploadedParsedId}
                      sourceFileName={gapFillData.sourceFileName}
                      content={gapFillData.content}
                      onPromoted={handlePromoted}
                    />
                  ) : (
                    // Layout-container — INGEN egen live-region: BrandSpinner bär
                    // redan role="status" + sr-only-label (annonseras EN gång; en
                    // nästlad live-region skulle dubbel-annonsera, design-Major).
                    <div className="flex flex-col items-center gap-3 py-8">
                      <BrandSpinner
                        size={48}
                        label={t("onboarding.gapFillLoading")}
                      />
                      <p className="text-body-sm text-text-secondary">
                        {t("onboarding.gapFillLoading")}
                      </p>
                    </div>
                  )}
                </div>
              </>
            ) : (
              // "done" — bekräftelse + val i EN slide (Klas: confirm + choice var
              // dubbelsteg). Grön check + "CV uppladdat" bara om ett CV faktiskt
              // laddades upp; annars rakt på matchnings-valet. EN DialogTitle bär
              // rubriken; status via ikon + text, aldrig färg ensam (WCAG 1.4.1).
              <>
                <div className="jp-welcome__confirm">
                  {uploaded && (
                    <CheckCircle2
                      className="jp-welcome__confirm-icon"
                      size={48}
                      aria-hidden="true"
                    />
                  )}
                  <DialogTitle
                    ref={titleRef}
                    tabIndex={-1}
                    className="jp-welcome__confirm-title"
                  >
                    {uploaded
                      ? t("onboarding.cvUploadedTitle")
                      : t("onboarding.setUpMatchTitle")}
                  </DialogTitle>
                  <DialogDescription className="jp-welcome__confirm-note">
                    {uploaded
                      ? t("onboarding.cvUploadedNote")
                      : t("onboarding.setUpMatchNote")}
                  </DialogDescription>
                </div>
                <div className="jp-welcome__foot">
                  <button
                    type="button"
                    className="jp-welcome__skip"
                    onClick={dismissWelcome}
                  >
                    {t("onboarding.skip")}
                  </button>
                  <span className="jp-welcome__foot-spacer" />
                  <Button type="button" onClick={openWizard}>
                    {t("onboarding.confirm")}
                  </Button>
                </div>
              </>
            )}
          </DialogContent>
        </Dialog>
      )}

      {/* Wizarden (welcome step 2) — samma komponent som /cv-prompten öppnar,
          två ingångar (ADR 0077). Cookien sätts HÄR när wizarden stängs (spara
          eller avbryt) — INTE i openWizard (server-action där hade re-renderat
          RSC:n och avmonterat modalen innan wizarden öppnats). Server-actionen
          re-rendrar /oversikt (showWelcome=false) så välkomsten inte återkommer;
          router.refresh är belt-and-suspenders. */}
      <MatchSetupWizard
        open={wizardOpen}
        onOpenChange={(next) => {
          setWizardOpen(next);
          if (!next) {
            startTransition(async () => {
              await markSetupWelcomeSeen();
              router.refresh();
            });
          }
        }}
        occupationFields={occupationFields}
        regions={regions}
        employmentTypes={employmentTypes}
        persistedOccupationGroups={persistedOccupationGroups}
        persistedRegions={persistedRegions}
        persistedMunicipalities={persistedMunicipalities}
        persistedEmploymentTypes={persistedEmploymentTypes}
        importCvHref={importCvHref}
        // STEG 1 / ADR 0079: CV:t är redan befordrat när wizarden öppnas, så
        // staging-artefakten (parsedResumeId) är soft-raderad. De rika
        // multi-signal-förslagen bärs i stället in som förhämtade kandidater.
        // Utan uppladdat CV → undefined (wizarden faller tillbaka på sin vanliga
        // latestRole-/noCv-väg).
        proposedOccupationGroups={uploaded ? proposedOccupations : undefined}
        onSaved={() => setWizardOpen(false)}
      />
    </>
  );
}

/** next-intl-translatorn för "settings"-namespacet (synkron i en klient-ö). */
type SettingsTranslator = ReturnType<typeof useTranslations<"settings">>;

/** Upload-stegets hjälptext (bär instruktionen — aldrig placeholder-exempel).
 * "done"-steget har sin copy inline (beror på om CV laddades upp). */
function stepIntro(t: SettingsTranslator, step: WelcomeStep): string {
  switch (step) {
    case "upload":
      return t("onboarding.uploadIntro");
    case "gapfill":
      // Gap-fill-steget renderar sin intro direkt (t("onboarding.gapFillIntro"));
      // grenen finns för uttömmande switch.
      return t("onboarding.gapFillIntro");
    case "done":
      return "";
  }
}
