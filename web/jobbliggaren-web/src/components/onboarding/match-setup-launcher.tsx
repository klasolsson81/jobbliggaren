"use client";

// "use client": klient-ö på /oversikt som äger matchnings-setup-modalens öppna-
// state och kör dismissal-cookien (markSetupWelcomeSeen) på close i en transition.
// Ersätter den gamla WelcomeSetupModal (epik #526): välkomst + CV-upload + wizard
// är nu ETT flöde i MatchSetupRailModal. Server-komponenten (/oversikt) avgör om
// modalen ska auto-öppnas (nytt konto utan angivet yrke, eller ?matchsetup=1 från
// notisen) och matar in taxonomi + persisterad SSOT som serialiserbara props.

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { MatchSetupRailModal } from "@/components/settings/match-setup-rail-modal";
import { markSetupWelcomeSeen } from "@/lib/onboarding/setup-welcome-actions";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";

interface MatchSetupLauncherProps {
  /** Auto-öppna vid mount (server avgör: showWelcome ELLER ?matchsetup=1). */
  readonly autoOpen: boolean;
  readonly occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  readonly regions: ReadonlyArray<TaxonomyRegion>;
  readonly employmentTypes: ReadonlyArray<TaxonomyOption>;
  readonly persistedOccupationGroups: ReadonlyArray<string>;
  readonly persistedRegions: ReadonlyArray<string>;
  readonly persistedMunicipalities: ReadonlyArray<string>;
  readonly persistedEmploymentTypes: ReadonlyArray<string>;
  readonly persistedSkills: ReadonlyArray<string>;
  readonly persistedOccupationExperience: ReadonlyArray<{
    readonly conceptId: string;
    readonly years: number | null;
  }>;
  readonly importCvHref: string;
}

/**
 * Match-setup-launcher (epik #526) — den enda mount-punkten på /oversikt.
 * Skriver inget själv utöver dismissal-cookien; den enda preferens-skrivningen är
 * rail-modalens befintliga MatchPreferences-PUT (som revalidate:ar /oversikt, så
 * notisen byter av sig själv efter en sparning).
 */
export function MatchSetupLauncher({
  autoOpen,
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
}: MatchSetupLauncherProps) {
  const router = useRouter();
  const [open, setOpen] = useState(autoOpen);
  const [, startTransition] = useTransition();

  function handleOpenChange(next: boolean) {
    setOpen(next);
    if (!next) {
      // Stäng/spara-klar: markera välkomsten sedd (auto-open-naggen återkommer
      // inte) och rensa ?matchsetup-parametern så en refresh/bakåt inte
      // återöppnar modalen. router.refresh läser om RSC:n (cookie + nya
      // preferenser efter en ev. sparning) — mirror den gamla welcome-modalen.
      startTransition(async () => {
        await markSetupWelcomeSeen();
        router.replace("/oversikt");
        router.refresh();
      });
    }
  }

  return (
    <MatchSetupRailModal
      open={open}
      onOpenChange={handleOpenChange}
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
    />
  );
}
