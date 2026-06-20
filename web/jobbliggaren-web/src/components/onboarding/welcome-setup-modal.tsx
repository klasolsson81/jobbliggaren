"use client";

// "use client": välkomst-modalen håller klient-only state (vilket steg, om
// wizarden är öppen), kopplar en Server Action (markSetupWelcomeSeen) på
// close/skip/Esc i en transition, och flyttar fokus programmatiskt till varje
// stegs rubrik (WCAG 2.4.3). Server-komponenten (/oversikt) avgör `showWelcome`
// och hämtar taxonomi + persisterade preferenser och skickar in dem som
// serialiserbara props. Inget av detta går i en Server Component.

import { useEffect, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
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

/** Welcome-modalens tre interna steg (klient-only — ingen route per steg). */
type WelcomeStep = "upload" | "confirm" | "choice";

interface WelcomeSetupModalProps {
  readonly showWelcome: boolean;
  // Wizard-data — samma serialiserbara props som /cv och /installningar matar
  // in i MatchSetupWizard (taxonomi + persisterad SSOT).
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
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
  persistedEmploymentTypes,
  importCvHref,
}: WelcomeSetupModalProps) {
  const router = useRouter();
  const [welcomeOpen, setWelcomeOpen] = useState(showWelcome);
  const [wizardOpen, setWizardOpen] = useState(false);
  const [step, setStep] = useState<WelcomeStep>("upload");
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
   * Öppna wizarden (welcome step 2). Markera sedd HÄR (användaren har engagerat
   * sig → naggar aldrig igen, även om wizarden avbryts) men UTAN router.refresh:
   * en refresh skulle om-evaluera `showWelcome=false` på servern och AVMONTERA
   * hela modalen (inkl. wizarden) mitt i övergången välkomst→wizard — då
   * försvann modalen och inget öppnades (buggen Klas såg). Cookien räcker; sidan
   * uppdateras när wizarden stängs (dess onOpenChange nedan).
   */
  function openWizard() {
    setWelcomeOpen(false);
    setWizardOpen(true);
    startTransition(async () => {
      await markSetupWelcomeSeen();
    });
  }

  return (
    <>
      <Dialog
        open={welcomeOpen}
        onOpenChange={(next) => {
          // Endast stängning hanteras här (scrim-klick, Esc, X). Öppning styrs
          // av server-proppen vid mount. Programmatisk stängning via openWizard
          // sätter `open=false` direkt och triggar INTE denna handler.
          if (!next) dismissWelcome();
        }}
      >
        <DialogContent
          className="jp-stdmodal jp-stdmodal--narrow"
          aria-describedby="welcome-setup-desc"
        >
          {step === "confirm" ? (
            // Bekräftelse: EN rubrik (DialogTitle bär "stort = typografisk
            // hierarki" per ADR 0077 A2.ii) med grön check ovanför + en not.
            // Ingen separat visuell rubrik-<p> (skulle bryta rubrikhierarkin).
            <>
              <div className="jp-welcome__confirm">
                <CheckCircle2
                  className="jp-welcome__confirm-icon"
                  size={48}
                  aria-hidden="true"
                />
                {/* Status bärs av text + ikon, aldrig färg ensam (WCAG 1.4.1). */}
                <DialogTitle
                  ref={titleRef}
                  tabIndex={-1}
                  className="jp-welcome__confirm-title"
                >
                  CV uppladdat
                </DialogTitle>
                <DialogDescription
                  id="welcome-setup-desc"
                  className="jp-welcome__confirm-note"
                >
                  Vi har läst in och tolkat ditt CV. Inget är ändrat och ingen
                  matchning är gjord ännu. Nästa steg är att ställa in din
                  matchningsprofil.
                </DialogDescription>
              </div>
              <div className="jp-welcome__foot">
                <span className="jp-welcome__foot-spacer" />
                <Button type="button" onClick={() => setStep("choice")}>
                  Fortsätt
                </Button>
              </div>
            </>
          ) : (
            <>
              <div className="jp-welcome__head">
                <DialogTitle
                  ref={titleRef}
                  tabIndex={-1}
                  className="jp-welcome__title"
                >
                  {step === "upload"
                    ? "Kom igång med matchning"
                    : "Ställ in din matchning"}
                </DialogTitle>
                <DialogDescription
                  id="welcome-setup-desc"
                  className="jp-welcome__intro"
                >
                  {stepIntro(step)}
                </DialogDescription>
              </div>

              {step === "upload" && (
                <div className="jp-welcome__body">
                  <CvUploadForm onUploaded={() => setStep("confirm")} />
                  <div className="jp-welcome__skiprow">
                    <button
                      type="button"
                      className="jp-welcome__skip"
                      onClick={() => setStep("choice")}
                    >
                      Fortsätt utan CV
                    </button>
                  </div>
                </div>
              )}

              {step === "choice" && (
                <div className="jp-welcome__body">
                  <div className="jp-welcome__foot">
                    <button
                      type="button"
                      className="jp-welcome__skip"
                      onClick={dismissWelcome}
                    >
                      Hoppa över
                    </button>
                    <span className="jp-welcome__foot-spacer" />
                    <Button type="button" onClick={openWizard}>
                      Ja, ställ in matchning
                    </Button>
                  </div>
                </div>
              )}
            </>
          )}
        </DialogContent>
      </Dialog>

      {/* Wizarden (welcome step 2) — samma komponent som /cv-prompten öppnar,
          två ingångar (ADR 0077). Cookien sattes redan i openWizard. När wizarden
          STÄNGS (spara eller avbryt) är flödet klart → router.refresh uppdaterar
          /oversikt (prefs satta + cookie satt → välkomsten visas inte igen). */}
      <MatchSetupWizard
        open={wizardOpen}
        onOpenChange={(next) => {
          setWizardOpen(next);
          if (!next) startTransition(() => router.refresh());
        }}
        occupationFields={occupationFields}
        regions={regions}
        employmentTypes={employmentTypes}
        persistedOccupationGroups={persistedOccupationGroups}
        persistedRegions={persistedRegions}
        persistedEmploymentTypes={persistedEmploymentTypes}
        importCvHref={importCvHref}
        onSaved={() => setWizardOpen(false)}
      />
    </>
  );
}

/** Stegets hjälptext (bär instruktionen — aldrig placeholder-exempel). Confirm
 * har sin egen not inline (DialogDescription i bekräftelse-blocket). */
function stepIntro(step: WelcomeStep): string {
  switch (step) {
    case "upload":
      return "Ladda upp ditt CV så kan vi föreslå vilka yrken du söker. Du väljer själv vad som tas med, och kan hoppa över det här.";
    case "confirm":
      return "";
    case "choice":
      return "Vill du ställa in din matchning nu? Det tar någon minut. Du kan hoppa över och göra det senare.";
  }
}
