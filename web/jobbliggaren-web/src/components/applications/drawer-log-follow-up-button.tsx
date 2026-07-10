"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { clampAnchoredTop } from "@/lib/applications/anchored-top";
import { LogFollowUpDialog } from "./log-follow-up-dialog";

interface DrawerLogFollowUpButtonProps {
  applicationId: string;
  contextTitle: string | null;
  contextCompany: string | null;
  toastCompany: string;
}

/**
 * Detaljpanelens "+ Lägg till" under UPPFÖLJNINGAR (#630 PR 7, design §8.6 —
 * Klas-låst 2026-07-05: prototyp-trogen; "Drawer"-namnet är ett PR 6-arv,
 * panelen är sedan 2026-07-10 den centrerade route-modalen). Öppnar "Logga
 * uppföljning"-dialogen (§9) ankrad nära klicket; det schemalagda
 * uppföljningsformuläret stannar på fullsidan. Egen dialog-instans (panelen
 * är ett eget React-träd, CTO-bind 6b: komposition — sektionen förblir
 * presentation).
 */
export function DrawerLogFollowUpButton({
  applicationId,
  contextTitle,
  contextCompany,
  toastCompany,
}: DrawerLogFollowUpButtonProps) {
  const tUi = useTranslations("applications.ui");
  const [state, setState] = useState<{ open: boolean; top: number | null }>({
    open: false,
    top: null,
  });

  return (
    <>
      <button
        type="button"
        className="jp-btn jp-btn--secondary"
        onClick={(e) => {
          const anchorY =
            e.clientY > 0
              ? e.clientY
              : e.currentTarget.getBoundingClientRect().top;
          setState({
            open: true,
            top:
              typeof window === "undefined"
                ? null
                : clampAnchoredTop(anchorY, window.innerHeight, {
                    offset: 170,
                    minVisible: 240,
                  }),
          });
        }}
      >
        {tUi("followUps.addLog")}
      </button>
      {state.open && (
        <LogFollowUpDialog
          open
          onOpenChange={(open) => {
            if (!open) setState({ open: false, top: null });
          }}
          applicationId={applicationId}
          contextTitle={contextTitle}
          contextCompany={contextCompany}
          toastCompany={toastCompany}
          top={state.top}
        />
      )}
    </>
  );
}
