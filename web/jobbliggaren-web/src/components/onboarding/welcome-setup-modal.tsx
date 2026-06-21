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

/** Welcome-modalens två interna steg (klient-only — ingen route per steg).
 * "done" slår ihop bekräftelse + val i EN slide (Klas: separat confirm + choice
 * kändes som dubbelsteg). */
type WelcomeStep = "upload" | "done";

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
  // Om ett CV faktiskt laddades upp (styr grön check + copy i "done"-steget;
  // "Fortsätt utan CV" hoppar till "done" UTAN check).
  const [uploaded, setUploaded] = useState(false);
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
      {/* Bara EN Radix-dialog i trädet åt gången: när wizarden öppnas tas
          välkomsten UR trädet (ej bara open=false). Annars togglas två dialoger
          samtidigt → focus/scroll-lock-krock → välkomsten stängs men wizarden
          monteras aldrig synligt (buggen Klas såg). */}
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
            {step === "upload" ? (
              <>
                <div className="jp-welcome__head">
                  <DialogTitle
                    ref={titleRef}
                    tabIndex={-1}
                    className="jp-welcome__title"
                  >
                    Kom igång med matchning
                  </DialogTitle>
                  <DialogDescription className="jp-welcome__intro">
                    {stepIntro("upload")}
                  </DialogDescription>
                </div>

                <div className="jp-welcome__body">
                  <CvUploadForm
                    onUploaded={() => {
                      setUploaded(true);
                      setStep("done");
                    }}
                  />
                  <div className="jp-welcome__skiprow">
                    <button
                      type="button"
                      className="jp-welcome__skip"
                      onClick={() => setStep("done")}
                    >
                      Fortsätt utan CV
                    </button>
                  </div>
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
                    {uploaded ? "CV uppladdat" : "Ställ in din matchning"}
                  </DialogTitle>
                  <DialogDescription className="jp-welcome__confirm-note">
                    {uploaded
                      ? "Vi har läst in och tolkat ditt CV. Inget är ändrat och ingen matchning är gjord ännu. Vill du ställa in din matchningsprofil nu?"
                      : "Vill du ställa in din matchning nu? Det tar någon minut. Du kan hoppa över och göra det senare."}
                  </DialogDescription>
                </div>
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
              </>
            )}
          </DialogContent>
        </Dialog>
      )}

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

/** Upload-stegets hjälptext (bär instruktionen — aldrig placeholder-exempel).
 * "done"-steget har sin copy inline (beror på om CV laddades upp). */
function stepIntro(step: WelcomeStep): string {
  switch (step) {
    case "upload":
      return "Ladda upp ditt CV så kan vi föreslå vilka yrken du söker. Du väljer själv vad som tas med, och kan hoppa över det här.";
    case "done":
      return "";
  }
}
