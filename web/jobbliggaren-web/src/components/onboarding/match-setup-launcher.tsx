"use client";

// Owns the opening snapshot, focus return and dismissal transition.
import { useRef, useState, useTransition, type ComponentProps } from "react";
import { useRouter } from "next/navigation";
import { MatchSetupRailModal } from "@/components/settings/match-setup-rail-modal";
import { markSetupWelcomeSeen } from "@/lib/onboarding/setup-welcome-actions";

export type MatchSetupData = Pick<ComponentProps<typeof MatchSetupRailModal>,
  "occupationFields" | "regions" | "employmentTypes" |
  "persistedOccupationGroups" | "persistedRegions" | "persistedMunicipalities" |
  "persistedRemote" | "persistedEmploymentTypes" | "persistedSkills" |
  "persistedSkillGroups" | "persistedOccupationExperience" | "importCvHref"
> & { readonly resumeStep: 0 | 1 };

interface MatchSetupLauncherProps {
  readonly request: "welcome" | "resume" | null;
  readonly data: MatchSetupData | null;
}

export function MatchSetupLauncher({ request, data }: MatchSetupLauncherProps) {
  const router = useRouter();
  const [, startTransition] = useTransition();
  const openerRef = useRef<HTMLElement | null>(null);
  const readyRequest = data === null ? null : request;
  const [opening, setOpening] = useState(() => ({
    request: readyRequest,
    open: readyRequest !== null,
    data,
    initialStep: request === "resume" ? data?.resumeStep : 0,
  }));

  // A new server read may remove setup eligibility while Save is still returning.
  // Keep the opening snapshot until close so the rail can present its Done receipt.
  if (readyRequest !== opening.request) {
    setOpening({
      request: readyRequest,
      open: opening.open || readyRequest !== null,
      data: opening.open ? opening.data : data,
      initialStep: opening.open ? opening.initialStep : request === "resume" ? data?.resumeStep : 0,
    });
  }

  function handleOpenChange(next: boolean) {
    setOpening((previous) => ({ ...previous, open: next }));
    if (!next) {
      startTransition(async () => {
        await markSetupWelcomeSeen();
        router.replace("/oversikt", { scroll: false });
        router.refresh();
      });
    }
  }

  function handleCloseAutoFocus(event: Event) {
    event.preventDefault();
    const opener = openerRef.current;
    const target = opener?.isConnected && opener !== document.body
      ? opener
      : document.getElementById("oversikt-continue-setup") ??
        document.getElementById("oversikt-card-matching");
    target?.focus();
  }

  if (opening.data === null) return null;

  return (
    <MatchSetupRailModal
      {...opening.data}
      open={opening.open}
      initialStep={opening.initialStep}
      onOpenChange={handleOpenChange}
      onOpenAutoFocus={() => {
        const active = document.activeElement;
        openerRef.current = active instanceof HTMLElement ? active : null;
      }}
      onCloseAutoFocus={handleCloseAutoFocus}
    />
  );
}
