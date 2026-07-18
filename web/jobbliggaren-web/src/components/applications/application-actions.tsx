"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  useTransition,
} from "react";
import { transitionStatusAction } from "@/lib/actions/applications";
import { showApplicationToast } from "@/lib/applications/toast-store";
import { clampAnchoredTop } from "@/lib/applications/anchored-top";
import type { ApplicationDto, ApplicationStatus } from "@/lib/dto/applications";
import { FinishDraftDialog } from "./finish-draft-dialog";
import { LogFollowUpDialog } from "./log-follow-up-dialog";
import { DeleteApplicationDialog } from "./delete-application-dialog";

/**
 * Dialogens topp ankras ~170px ovanför klickpunkten (design §9 — "aldrig fast
 * topposition"). Klampad mot viewporten via clampAnchoredTop (CTO-bind 5, DRY).
 */
const DIALOG_ANCHOR_OFFSET = 170;
const DIALOG_MIN_VISIBLE = 240;

/** Klick-Y → klampad dialogtopp (ren modul-funktion — stabil över renders). */
function anchoredTop(anchorY: number | null): number | null {
  if (anchorY == null || anchorY <= 0 || typeof window === "undefined") {
    return null;
  }
  return clampAnchoredTop(anchorY, window.innerHeight, {
    offset: DIALOG_ANCHOR_OFFSET,
    minVisible: DIALOG_MIN_VISIBLE,
  });
}

interface DialogState {
  kind: "finishDraft" | "logFollowUp" | "delete";
  application: ApplicationDto;
  top: number | null;
}

export interface ApplicationActionsValue {
  /**
   * Direkt statusbyte (design §9 "direktbyten utan dialog"): persistas
   * omedelbart via den auditerade servern-actionen; vid framgång publiceras
   * ångra-toasten (ADR 0092 D3 — ångra är en kompenserande invers transition).
   */
  transition: (application: ApplicationDto, target: ApplicationStatus) => void;
  /** "Slutför och skicka"-dialogen (utkast, design §9). anchorY = klickets viewport-Y. */
  openFinishDraft: (application: ApplicationDto, anchorY: number | null) => void;
  /** "Logga uppföljning"-dialogen (design §9). anchorY = klickets viewport-Y. */
  openLogFollowUp: (application: ApplicationDto, anchorY: number | null) => void;
  /**
   * #782 (ADR 0104) — "Ta bort ansökan": opens the destructive HARD-delete
   * confirm (ONE shared centered dialog on the island, never N per row). No
   * anchorY — a destructive confirm is centered (WithdrawApplicationButton
   * precedent), not click-anchored like the two dialogs above.
   */
  deleteApplication: (application: ApplicationDto) => void;
}

const ApplicationActionsContext = createContext<ApplicationActionsValue | null>(
  null,
);

/**
 * pendingIds (id:n med pågående statusbyte, disable:ar radens knappar — ett SET
 * så två överlappande byten på olika rader inte återaktiverar varandra i förtid)
 * ligger i ett EGET context, SKILT från de stabila action-funktionerna ovan
 * (perf-audit d4). Set:et byter identitet vid varje statusbyte (add + delete);
 * hade det legat i samma value som funktionerna hade varenda `useApplicationActions`-
 * konsument re-renderat två gånger per byte. Nu prenumererar bara vy-containrarna
 * (StatusSection / ApplicationsTable / AttentionQueue / ApplicationBoardCard) på
 * Set:et och trådar ett boolean `pending`-prop ned till de memo-lindade löven →
 * vid ett byte re-renderar bara den togglade raden, aldrig hela listan.
 */
const ApplicationPendingContext = createContext<ReadonlySet<string> | null>(
  null,
);

export function useApplicationActions(): ApplicationActionsValue {
  const value = useContext(ApplicationActionsContext);
  if (value == null) {
    throw new Error(
      "useApplicationActions must be used within <ApplicationActionsProvider>",
    );
  }
  return value;
}

/**
 * Prenumererar på pendingIds-Set:et (perf-audit d4). Anropas ENBART av
 * vy-containrarna som trådar ett per-rad `pending`-prop till löven — aldrig av
 * ett memo-lindat löv självt (då hade context-prenumerationen kringgått memon och
 * re-renderat lövet vid varje statusbyte, vilket är precis defekten d4 stänger).
 */
export function useApplicationPending(): ReadonlySet<string> {
  const value = useContext(ApplicationPendingContext);
  if (value == null) {
    throw new Error(
      "useApplicationPending must be used within <ApplicationActionsProvider>",
    );
  }
  return value;
}

/** Visningsnamn för toasten "{company}: …" — företag, annars radens korta id. */
export function applicationDisplayName(application: ApplicationDto): string {
  return application.jobAd?.company ?? `#${application.id.slice(0, 8)}`;
}

/**
 * ApplicationActionsProvider (#630 PR 7) — äger mutations-plumbingen för
 * pipeline-öns action-affordanser (radknappar, statusmeny, kökortens CTA):
 * transition + toast + de TVÅ dialogerna som EN instans vardera på öns nivå
 * (prototypens `dialog {kind, appId, top}`-modell — aldrig N monterade dialoger).
 *
 * Mutations-UX per CTO-bind 1: await server action → revalidatePath-driven
 * server-recompute (attention/grupper är BE-SSOT — ingen optimistisk
 * grupp-flytt), pending-state per rad under tiden. Detaljmodalen har sina egna
 * öar (DrawerStatusActions — PR 6-arvsnamn) — samma actions, samma toast-store,
 * ingen delad React-state behövs över träden.
 */
export function ApplicationActionsProvider({
  children,
}: {
  children: React.ReactNode;
}) {
  const [pendingIds, setPendingIds] = useState<ReadonlySet<string>>(
    () => new Set(),
  );
  const [dialog, setDialog] = useState<DialogState | null>(null);
  const [, startTransition] = useTransition();

  const transition = useCallback(
    (application: ApplicationDto, target: ApplicationStatus) => {
      if (target === application.status) return;
      setPendingIds((prev) => new Set(prev).add(application.id));
      startTransition(async () => {
        const result = await transitionStatusAction(application.id, target);
        if (result.success) {
          showApplicationToast({
            kind: "statusChange",
            applicationId: application.id,
            company: applicationDisplayName(application),
            from: application.status,
            to: target,
          });
        } else {
          showApplicationToast({ kind: "error", message: result.error });
        }
        setPendingIds((prev) => {
          const next = new Set(prev);
          next.delete(application.id);
          return next;
        });
      });
    },
    [],
  );

  const openFinishDraft = useCallback(
    (application: ApplicationDto, anchorY: number | null) => {
      setDialog({ kind: "finishDraft", application, top: anchoredTop(anchorY) });
    },
    [],
  );

  const openLogFollowUp = useCallback(
    (application: ApplicationDto, anchorY: number | null) => {
      setDialog({ kind: "logFollowUp", application, top: anchoredTop(anchorY) });
    },
    [],
  );

  const deleteApplication = useCallback((application: ApplicationDto) => {
    setDialog({ kind: "delete", application, top: null });
  }, []);

  // Bara de stabila funktionerna — pendingIds är UTE ur denna value (d4). Alla
  // deps är useCallback([]) → value:n är referens-stabil över öns livstid, så
  // ingen `useApplicationActions`-konsument re-renderar vid ett statusbyte.
  const value = useMemo<ApplicationActionsValue>(
    () => ({
      transition,
      openFinishDraft,
      openLogFollowUp,
      deleteApplication,
    }),
    [transition, openFinishDraft, openLogFollowUp, deleteApplication],
  );

  const closeDialog = (open: boolean) => {
    if (!open) setDialog(null);
  };

  return (
    <ApplicationActionsContext.Provider value={value}>
      <ApplicationPendingContext.Provider value={pendingIds}>
        {children}
      </ApplicationPendingContext.Provider>
      {dialog?.kind === "finishDraft" && (
        <FinishDraftDialog
          open
          onOpenChange={closeDialog}
          application={dialog.application}
          top={dialog.top}
        />
      )}
      {dialog?.kind === "logFollowUp" && (
        <LogFollowUpDialog
          open
          onOpenChange={closeDialog}
          applicationId={dialog.application.id}
          contextTitle={dialog.application.jobAd?.title ?? null}
          contextCompany={dialog.application.jobAd?.company ?? null}
          toastCompany={applicationDisplayName(dialog.application)}
          top={dialog.top}
        />
      )}
      {dialog?.kind === "delete" && (
        <DeleteApplicationDialog
          open
          onOpenChange={closeDialog}
          applicationId={dialog.application.id}
        />
      )}
    </ApplicationActionsContext.Provider>
  );
}
