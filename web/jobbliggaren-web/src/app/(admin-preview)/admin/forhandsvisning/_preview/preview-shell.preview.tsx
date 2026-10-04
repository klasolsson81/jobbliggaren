"use client";

import { createContext, useContext, useState, type ReactNode } from "react";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { ADMIN_REGION_KINDS, type AdminRegionKind } from "@/lib/admin/view-models";
import { AdminImpersonationBanner } from "@/components/admin/admin-impersonation-banner";
import { AdminSegment } from "@/components/admin/admin-segment";

interface PreviewState {
  readonly kind: AdminRegionKind;
}

const PreviewStateContext = createContext<PreviewState>({ kind: "loaded" });

/** The region state the band chose, for every preview page below it. */
export function usePreviewState(): PreviewState {
  return useContext(PreviewStateContext);
}

/**
 * The preview's band (ADR 0150 D5): it says what the page is, picks the state the page's regions
 * show, and turns the impersonation banner on and off. Its state lives in this tab's memory only.
 * The state choice is offered only on the pages whose regions follow it.
 */
export function PreviewShell({
  impersonatedEmail,
  statefulPaths,
  children,
}: {
  readonly impersonatedEmail: string;
  readonly statefulPaths: ReadonlyArray<string>;
  readonly children: ReactNode;
}) {
  const t = useTranslations("admin-preview.band");
  const pathname = usePathname();
  const [kind, setKind] = useState<AdminRegionKind>("loaded");
  const [impersonating, setImpersonating] = useState(false);

  return (
    <PreviewStateContext.Provider value={{ kind }}>
      <aside className="jp-adminpreview-band" aria-label={t("label")}>
        <div className="jp-adminpreview-band__inner">
          <p className="jp-adminpreview-band__text">{t("text")}</p>
          <div className="jp-adminpreview-band__controls">
            {statefulPaths.includes(pathname) ? (
              <AdminSegment
                label={t("stateLabel")}
                value={kind}
                onChange={setKind}
                options={ADMIN_REGION_KINDS.map((value) => ({ value, label: t(`states.${value}`) }))}
              />
            ) : null}
            <button
              type="button"
              className="jp-btn jp-btn--sm jp-btn--secondary"
              aria-pressed={impersonating}
              onClick={() => setImpersonating((on) => !on)}
            >
              {t("impersonation")}
            </button>
          </div>
        </div>
      </aside>
      {impersonating ? (
        <AdminImpersonationBanner email={impersonatedEmail} onEnd={() => setImpersonating(false)} />
      ) : null}
      {children}
    </PreviewStateContext.Provider>
  );
}
