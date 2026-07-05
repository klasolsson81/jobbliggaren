"use client";

import { useEffect, useRef, useSyncExternalStore, useTransition } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { transitionStatusAction } from "@/lib/actions/applications";
import { applicationStatusLabel } from "@/lib/applications/status";
import {
  dismissApplicationToast,
  getApplicationToastServerSnapshot,
  getApplicationToastSnapshot,
  showApplicationToast,
  subscribeApplicationToast,
  type ApplicationToast,
} from "@/lib/applications/toast-store";

/** Auto-close per design handoff §10. */
const AUTO_CLOSE_MS = 8_000;

/**
 * ApplicationToastHost — the single renderer of the /ansokningar action toast
 * (#630 PR 7, design §10; CTO-bind 2: ONE host, mounted in the (app) layout so
 * both the pipeline island and the intercepting-route drawer publish to the
 * same surface; reused by PR 8 drag + PR 10 bulk).
 *
 * - Status toast: "{company}: {from} → {to}" + Ångra (--jp-gold, underlined) + ✕.
 *   Undo = a compensating inverse TransitionTo(previous) via the SAME audited
 *   server action (ADR 0092 D3) — it appends a new timeline row and resets the
 *   day counter; it does NOT erase history (honest-copy bind: the toast claims
 *   only the status revert, never "restores days").
 * - Follow-up toast: no Ångra (design §10).
 * - Error toast: assertive live region, no Ångra.
 * - Auto-close 8 s, paused while hovered or focused (WCAG 2.2.1 — the user must
 *   be able to keep the actionable Ångra available while interacting with it).
 *
 * The aria-live regions are ALWAYS mounted (empty when no toast) so screen
 * readers reliably announce content changes into them.
 */
export function ApplicationToastHost() {
  const toast = useSyncExternalStore(
    subscribeApplicationToast,
    getApplicationToastSnapshot,
    getApplicationToastServerSnapshot,
  );

  return (
    <>
      <div aria-live="polite" role="status">
        {toast != null && toast.kind !== "error" && <ToastCard toast={toast} />}
      </div>
      <div aria-live="assertive" role="alert">
        {toast != null && toast.kind === "error" && <ToastCard toast={toast} />}
      </div>
    </>
  );
}

function ToastCard({ toast }: { toast: ApplicationToast }) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const [undoPending, startUndo] = useTransition();
  // Paus-flagga för auto-close: hover/fokus håller toasten öppen; när
  // interaktionen släpper startas HELA 8s-fönstret om (enkelt och förutsägbart).
  const pausedRef = useRef(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const { token } = toast;

  useEffect(() => {
    const arm = () => {
      if (timerRef.current != null) clearTimeout(timerRef.current);
      timerRef.current = setTimeout(() => {
        if (!pausedRef.current) dismissApplicationToast(token);
      }, AUTO_CLOSE_MS);
    };
    arm();
    return () => {
      if (timerRef.current != null) clearTimeout(timerRef.current);
    };
  }, [token]);

  const pause = () => {
    pausedRef.current = true;
    if (timerRef.current != null) clearTimeout(timerRef.current);
  };
  const resume = () => {
    pausedRef.current = false;
    if (timerRef.current != null) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(
      () => dismissApplicationToast(token),
      AUTO_CLOSE_MS,
    );
  };

  const message = ((): string => {
    switch (toast.kind) {
      case "statusChange":
        return tUi("toast.statusChange", {
          company: toast.company,
          from: applicationStatusLabel(t, toast.from),
          to: applicationStatusLabel(t, toast.to),
        });
      case "followUpLogged":
        return tUi("toast.followUpLogged", { company: toast.company });
      case "error":
        return toast.message;
    }
  })();

  const undo = () => {
    if (toast.kind !== "statusChange") return;
    startUndo(async () => {
      // Kompenserande invers transition (ADR 0092 D3) — samma auditerade
      // action; revalidatePath uppdaterar lista + drawer server-side.
      const result = await transitionStatusAction(
        toast.applicationId,
        toast.from,
      );
      if (result.success) {
        // Ingen kedjad ångra-toast (CTO-bind 3, prototyp-exakt).
        dismissApplicationToast(token);
      } else {
        showApplicationToast({ kind: "error", message: result.error });
      }
    });
  };

  return (
    <div
      className={
        toast.kind === "error" ? "jp-toast jp-toast--error" : "jp-toast"
      }
      onMouseEnter={pause}
      onMouseLeave={resume}
      onFocus={pause}
      onBlur={resume}
    >
      <span className="jp-toast__msg">{message}</span>
      {toast.kind === "statusChange" && (
        <button
          type="button"
          className="jp-toast__undo"
          onClick={undo}
          disabled={undoPending}
        >
          {undoPending ? tUi("toast.undoing") : tUi("toast.undo")}
        </button>
      )}
      <button
        type="button"
        className="jp-toast__close"
        aria-label={tUi("toast.dismissAriaLabel")}
        onClick={() => dismissApplicationToast(token)}
      >
        <X size={16} aria-hidden="true" />
      </button>
    </div>
  );
}
