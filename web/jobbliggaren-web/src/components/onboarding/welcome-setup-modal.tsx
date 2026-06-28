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
import { CvUploadForm } from "@/components/resumes/cv-upload-form";
import { MatchSetupWizard } from "@/components/settings/match-setup-wizard";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import { markSetupWelcomeSeen } from "@/lib/onboarding/setup-welcome-actions";

/** Welcome-modalens interna steg (klient-only — ingen route per steg).
 * #251: ett `welcome`-steg (sida 1) inleder flödet med en kort rekommendation
 * att ställa in matchningen, FÖRE CV-uppladdningen (sida 2). Rubriken på
 * upload-steget namnger steget ("Ladda upp CV"), inte hela flödet.
 * Onboarding-frikoppling (DEL 1 / ADR 0074-amend): gap-fill-steget är BORTTAGET.
 * Det uppladdade CV:t befordras INTE i välkomstflödet — det lever kvar som en
 * PendingReview-artefakt och ytas som ett "slutför ditt CV"-kort på /cv. Upload
 * går därför rakt till "done" (bekräftelse + matchnings-val i EN slide). */
type WelcomeStep = "welcome" | "upload" | "done";

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
  /** STEG 3 / ADR 0079: kompetens-axeln (pre-fill för wizarden). */
  readonly persistedSkills: ReadonlyArray<string>;
  /** exp-per-occ (ADR 0079-amendment PR-4): per-yrke-erfarenhets-overlay (pre-fill). */
  readonly persistedOccupationExperience: ReadonlyArray<{
    readonly conceptId: string;
    readonly years: number | null;
  }>;
  /** CV-importflödets route (wizardens yrkes-steg tom-state-länk). */
  readonly importCvHref: string;
}

/**
 * Välkomst-/första-setup-modal (ADR 0077 STEG 5). Triggas av server-signaler
 * (profil utan angett yrke + ingen setup-welcome-cookie) och avfärdas via en
 * `__Host-`-funktionscookie — ingen ny backend-kolumn (ADR 0077 / ADR 0076
 * Decision 3).
 *
 * Onboarding-frikoppling (DEL 1, CTO-bind pending-card): flödet tvingar INTE
 * längre en gap-fill innan användaren kan gå vidare. Det uppladdade CV:t LÄSES
 * men SPARAS INTE i välkomstflödet — det stannar som en PendingReview-artefakt
 * och ytas som ett "slutför ditt CV"-kort på /cv. Flödet (#251 / Klas-vision):
 * sida 1 "Välkommen" (kort rekommendation) → ladda upp CV → ärlig bekräftelse
 * "CV inläst" (inte "sparat") → val "ställa in matchning nu?" → wizarden.
 * Eftersom CV:t INTE befordras lever staging-artefakten kvar, så wizarden
 * auto-föreslår live ur det (parsedResumeId).
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
  persistedSkills,
  persistedOccupationExperience,
  importCvHref,
}: WelcomeSetupModalProps) {
  const t = useTranslations("settings");
  const router = useRouter();
  const [welcomeOpen, setWelcomeOpen] = useState(showWelcome);
  const [wizardOpen, setWizardOpen] = useState(false);
  // #251: starta på sida 1 ("Välkommen") — en kort rekommendation före uppladdning.
  const [step, setStep] = useState<WelcomeStep>("welcome");
  // Om ett CV faktiskt laddades upp (styr grön check + "CV inläst"-copy i
  // "done"-steget; "Fortsätt utan CV" hoppar direkt till "done" UTAN check).
  const [uploaded, setUploaded] = useState(false);
  // Id för det just uppladdade parsed_resume:t. CV:t befordras INTE här, så
  // staging-artefakten lever — id:t bärs till wizarden så dess yrkes-/kompetens-
  // steg auto-föreslår live ur den pending-parsade artefakten.
  const [uploadedParsedId, setUploadedParsedId] = useState<string | null>(null);
  const [, startTransition] = useTransition();

  // Fokus till stegrubriken EFTER commit (WCAG 2.4.3 + 4.1.3). En effekt på
  // `step` garanterar att den nya DialogTitle är monterad när `.focus()` körs —
  // så skärmläsaren annonserar den nya rubriken (t.ex. "CV inläst" vid lyckad
  // uppladdning). En queueMicrotask i event-handlern kunde köra före React
  // committat den nya titeln (text-mutation på redan fokuserad nod annonseras
  // inte). `welcomeOpen`-guarden hindrar fokus-stöld när wizarden tar över.
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
   * Upload klar → rakt till "done" (bekräftelse + matchnings-val). CV:t
   * befordras INTE (onboarding-frikoppling, CTO-bind pending-card): det stannar
   * som en PendingReview-artefakt och ytas som ett "slutför ditt CV"-kort på
   * /cv. Eftersom staging-artefakten lever kan wizarden auto-föreslå live ur
   * den (uploadedParsedId → MatchSetupWizard.parsedResumeId).
   */
  function handleCvUploaded(parsedResumeId: string) {
    setUploadedParsedId(parsedResumeId);
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
          <DialogContent className="jp-stdmodal jp-stdmodal--narrow">
            {step === "welcome" ? (
              // #251 sida 1 — "Välkommen": en kort, lugn rekommendation att
              // ställa in matchningen. Centrerad framing (samma som "done") så
              // de inramande sidorna känns sammanhängande; uppladdningen (sida 2)
              // bär det vanliga formulär-huvudet. DialogTitle får programmatisk
              // fokus vid stegbyte (WCAG 2.4.3).
              <>
                <div className="jp-welcome__confirm">
                  <DialogTitle
                    ref={titleRef}
                    tabIndex={-1}
                    className="jp-welcome__confirm-title"
                  >
                    {t("onboarding.welcomeTitle")}
                  </DialogTitle>
                  <DialogDescription className="jp-welcome__confirm-note">
                    {t("onboarding.welcomeBody")}
                  </DialogDescription>
                </div>
                <div className="jp-welcome__foot">
                  <button
                    type="button"
                    className="jp-welcome__skip"
                    onClick={dismissWelcome}
                  >
                    {t("onboarding.welcomeLater")}
                  </button>
                  <span className="jp-welcome__foot-spacer" />
                  <Button type="button" onClick={() => setStep("upload")}>
                    {t("onboarding.welcomeStart")}
                  </Button>
                </div>
              </>
            ) : step === "upload" ? (
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
                    {t("onboarding.uploadIntro")}
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
            ) : (
              // "done" — bekräftelse + val i EN slide (Klas: confirm + choice var
              // dubbelsteg). Grön check + ärlig "CV inläst"-copy bara om ett CV
              // faktiskt laddades upp; annars rakt på matchnings-valet. EN
              // DialogTitle bär rubriken; status via ikon + text, aldrig färg
              // ensam (WCAG 1.4.1). Copyn är ärlig (CTO-bind): CV:t är INLÄST,
              // inte sparat — den gröna checken signalerar "läst OK", inte "sparat".
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
                      ? t("onboarding.cvReadTitle")
                      : t("onboarding.setUpMatchTitle")}
                  </DialogTitle>
                  <DialogDescription className="jp-welcome__confirm-note">
                    {uploaded
                      ? t("onboarding.cvReadNote")
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
        persistedSkills={persistedSkills}
        persistedOccupationExperience={persistedOccupationExperience}
        importCvHref={importCvHref}
        // Onboarding-frikoppling (CTO-bind pending-card): CV:t befordras INTE i
        // välkomstflödet, så staging-artefakten lever. Bär in parsedResumeId så
        // wizardens yrkes-/kompetens-steg auto-föreslår live ur den pending-
        // parsade artefakten. Utan uppladdat CV → undefined (wizarden faller
        // tillbaka på sin vanliga latestRole-/noCv-väg).
        parsedResumeId={uploadedParsedId ?? undefined}
        // Inga förhämtade förslag bärs in: med en levande staging-artefakt
        // auto-föreslår OccupationSection/SkillSection själva när proposed* är
        // undefined OCH parsedResumeId är satt (se MatchSetupWizard-wiring).
        proposedOccupationGroups={undefined}
        proposedSkills={undefined}
        onSaved={() => setWizardOpen(false)}
      />
    </>
  );
}
