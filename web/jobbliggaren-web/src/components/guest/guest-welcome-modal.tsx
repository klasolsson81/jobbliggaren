"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { markGuestWelcomeSeen } from "@/lib/guest/guest-mode-actions";

// F-Pre Punkt 5 — Första-gångs-välkomst-modal i gäst-mode (Klas-direktiv §H
// + CTO-dom 2026-05-24 Beslut 4).
//
// Server passar `showWelcome={true}` (cookie saknades vid SSR). On-close
// anropar Server Action som sätter `__Host-jobbliggaren_guest_welcomed`-
// cookien så modalen inte återkommer. Civic-utility-ton: rak svenska, inga
// emoji, inga utropstecken. EN primärknapp "Börja utforska" (design-reviewer
// B2 2026-05-24 + code-reviewer M1: två identiska "Utforska"-knappar bröt
// WCAG 2.4.6 + DESIGN.md §6 "en primary per form"; Klas-direktiv var
// "Okej eller Utforska — du kan bestämma något lämpligt"). Stänger även via
// Escape eller X-knapp (Radix Dialog-default per
// `components/ui/dialog.tsx:66-69`).

interface GuestWelcomeModalProps {
  readonly showWelcome: boolean;
}

export function GuestWelcomeModal({ showWelcome }: GuestWelcomeModalProps) {
  const [open, setOpen] = useState(showWelcome);
  const [, startTransition] = useTransition();
  const router = useRouter();

  const handleClose = () => {
    setOpen(false);
    // Server Action — sätter cookien så modalen inte återkommer. Wrappa i
    // transition så UI:t inte väntar på round-trip. router.refresh() läser
    // om RSC-trädet så `showWelcome` blir false vid nästa navigation utan
    // ny page-load.
    startTransition(async () => {
      await markGuestWelcomeSeen();
      router.refresh();
    });
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) handleClose();
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Välkommen till demoläget</DialogTitle>
          <DialogDescription>
            Du utforskar Jobbliggaren utan att logga in. Allt innehåll är
            exempeldata, inga riktiga ansökningar eller CV.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-3 text-body-sm text-text-primary">
          <p>Det här kan du göra som gäst:</p>
          <ul className="ml-4 list-disc space-y-1 text-text-secondary">
            <li>Bläddra i översikten över ansökningar och CV</li>
            <li>Se hur pipeline-vyn fungerar</li>
            <li>Granska CV-varianter och formgivning</li>
          </ul>
          <p>Det här kräver ett konto:</p>
          <ul className="ml-4 list-disc space-y-1 text-text-secondary">
            <li>Söka och spara annonser från Platsbanken</li>
            <li>Skapa eller redigera CV och ansökningar</li>
            <li>Få notiser och uppföljning</li>
          </ul>
        </div>

        <DialogFooter>
          <button
            type="button"
            className="jp-btn jp-btn--primary"
            onClick={handleClose}
          >
            Börja utforska
          </button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
