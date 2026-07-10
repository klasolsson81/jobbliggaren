import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /ansokningar (#739 — finding
 * `p1-no-loading-tsx-any-primary-route` P0). Paints the pagehero + the
 * applications-list ledger shape immediately on navigation, instead of freezing
 * the previous page.
 *
 * Re-uses `jp-pagehero` + `jp-section` structural classes; the aside mirrors the
 * single "Ny ansökan" primary action. Also covers the deeper `/ansokningar/[id]`
 * route only until that segment's own `loading.tsx` (the detail skeleton) takes
 * over. sr-only `role="status"` announces; visuals decorative. Sync RSC.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.ansokningar")}
      </span>

      <PageHeroSkeleton aside={<span className="jp-skeleton block h-10 w-36" />} />

      <div className="jp-container jp-page" aria-hidden="true">
        {/* Sekundära actions (Statistik / Aktivitetsrapport), högerställda */}
        <div className="mb-6 flex flex-wrap justify-end gap-3">
          <span className="jp-skeleton block h-9 w-28" />
          <span className="jp-skeleton block h-9 w-40" />
        </div>

        <section className="jp-section">
          <div className="jp-section__head">
            <span className="jp-skeleton block h-5 w-48" />
            <span className="jp-skeleton block h-4 w-12" />
          </div>
          <div className="flex flex-col gap-4">
            {[0, 1, 2, 3, 4].map((row) => (
              <div
                key={row}
                className="flex items-center justify-between gap-4"
              >
                <div className="flex min-w-0 flex-1 flex-col gap-2">
                  <span className="jp-skeleton block h-4 w-1/2 max-w-full" />
                  <span className="jp-skeleton block h-3 w-1/3 max-w-full" />
                </div>
                <span className="jp-skeleton block h-4 w-20 shrink-0" />
              </div>
            ))}
          </div>
        </section>
      </div>
    </>
  );
}
