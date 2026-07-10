"use client";

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";
import { transitionStatusAction } from "@/lib/actions/applications";
import {
  ACTIVE_PATH_STATUSES,
  applicationStatusLabel,
  nextStepOf,
  PARK_STATUSES,
} from "@/lib/applications/status";
import { showApplicationToast } from "@/lib/applications/toast-store";
import type { ApplicationStatus } from "@/lib/dto/applications";

interface DrawerStatusActionsProps {
  applicationId: string;
  status: ApplicationStatus;
  /** Visningsnamn för toasten ("{company}: {från} → {till}"). */
  displayName: string;
}

/**
 * Detaljpanelens statusmaskineri (#630 PR 7, design §8.3–8.5; "Drawer"-namnet
 * är ett PR 6-arv — panelen är sedan 2026-07-10 den centrerade route-modalen,
 * ADR 0092 Livscykel-amendment). Klient-ö renderad av den RSC-ägda
 * ApplicationDrawerBody (serialiserbara props över gränsen):
 *
 *  - §8.3 Primär-CTA "Flytta till {nästa}" (fylld accent-800, h38) + "Alla
 *    byten kan ångras." — Ghosted: "Återaktivera som Skickad" (prototyp-facit);
 *    terminala: ingen CTA.
 *  - §8.4 Stegväljare: de 7 stegen på aktiva vägen, KLICKBARA ÄVEN BAKÅT =
 *    direkt transition (ADR 0092 D3 fria byten; nuvarande steg disabled —
 *    self-transition är en tyst no-op).
 *  - §8.5 AVSLUTA ELLER PARKERA: Nekad (dangertext) / Återtagen / Ghosted.
 *
 * Alla byten: persist-immediately via den auditerade servern-actionen →
 * revalidatePath server-recompute (CTO-bind 1; detaljmodalen re-renderas i sin
 * route) → ångra-toast (kompenserande invers, CTO-bind 3). Fel visas inline i
 * panelen (role="alert").
 */
export function DrawerStatusActions({
  applicationId,
  status,
  displayName,
}: DrawerStatusActionsProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  const currentIndex = ACTIVE_PATH_STATUSES.indexOf(status);

  const move = (target: ApplicationStatus) => {
    if (target === status || isPending) return;
    setError(null);
    startTransition(async () => {
      const result = await transitionStatusAction(applicationId, target);
      if (result.success) {
        showApplicationToast({
          kind: "statusChange",
          applicationId,
          company: displayName,
          from: status,
          to: target,
        });
      } else {
        setError(result.error);
      }
    });
  };

  const next = nextStepOf(status);
  const ctaLabel =
    status === "Ghosted"
      ? tUi("drawer.reactivate")
      : next != null
        ? tUi("drawer.moveToNext", { status: applicationStatusLabel(t, next) })
        : null;

  return (
    <div className="jp-drawer-actions">
      {/* §8.3 Primär-CTA + ångra-löftet (ADR 0092 D3 gör löftet sant: varje
          byte kan följas av en kompenserande invers). */}
      {ctaLabel != null && next != null && (
        <div className="jp-drawer-actions__cta">
          <button
            type="button"
            className="jp-btn jp-btn--primary jp-drawer-cta"
            disabled={isPending}
            onClick={() => move(next)}
          >
            {ctaLabel}
          </button>
          <p className="jp-drawer-cta__hint">{tUi("drawer.undoHint")}</p>
        </div>
      )}

      {/* §8.4 Stegväljare — 7 steg, även bakåt. */}
      <section aria-labelledby="jp-drawer-steps-label">
        <div className="jp-section-label" id="jp-drawer-steps-label">
          {tUi("drawer.flowLabel")}
          <span className="jp-section-label__hint">
            {tUi("drawer.flowHint")}
          </span>
        </div>
        <ol className="jp-steppicker">
          {ACTIVE_PATH_STATUSES.map((step, index) => {
            const stepState =
              currentIndex === -1
                ? "future"
                : index < currentIndex
                  ? "done"
                  : index === currentIndex
                    ? "current"
                    : "future";
            return (
              <li key={step}>
                <button
                  type="button"
                  className="jp-steppicker__step"
                  data-state={stepState}
                  disabled={stepState === "current" || isPending}
                  aria-current={stepState === "current" ? "step" : undefined}
                  onClick={() => move(step)}
                >
                  <span className="jp-steppicker__circle" aria-hidden="true">
                    {stepState === "done" ? <Check size={14} /> : index + 1}
                  </span>
                  <span className="jp-steppicker__name">
                    {applicationStatusLabel(t, step)}
                  </span>
                  {stepState === "current" && (
                    <span className="jp-steppicker__nu jp-mono">
                      {tUi("drawer.nowChip")}
                    </span>
                  )}
                </button>
              </li>
            );
          })}
        </ol>
      </section>

      {/* §8.5 Avsluta eller parkera. */}
      <section aria-labelledby="jp-drawer-park-label">
        <div className="jp-section-label" id="jp-drawer-park-label">
          {tUi("drawer.parkLabel")}
        </div>
        <div className="jp-parkrow">
          {PARK_STATUSES.map((park) => {
            const active = park === status;
            return (
              <button
                key={park}
                type="button"
                className={
                  park === "Rejected"
                    ? "jp-parkbtn jp-parkbtn--danger"
                    : "jp-parkbtn"
                }
                aria-pressed={active}
                disabled={active || isPending}
                onClick={() => move(park)}
              >
                {applicationStatusLabel(t, park)}
              </button>
            );
          })}
        </div>
      </section>

      {error != null && (
        <p role="alert" className="text-body-sm text-danger-600">
          {error}
        </p>
      )}
    </div>
  );
}
