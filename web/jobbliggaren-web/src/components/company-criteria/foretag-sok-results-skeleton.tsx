import { useTranslations } from "next-intl";
import { Announce } from "@/components/company-criteria/foretag-sok-announcer";

/**
 * #560 PR-B — the loading state for the `/foretag/sok` results region. Rendered as the `<Suspense>`
 * fallback around the results Server Component (and by the route `loading.tsx`) — ONLY the results
 * swap to the skeleton while a search runs; the pagehero + filter panel + org.nr island stay rendered.
 * Civic-utility: flat neutral grey rows (`.jp-skeleton`), no shimmer, no pulse, no spinner.
 *
 * a11y (#1092): the visible sentence is ordinary content and this element is NOT a live region.
 * It cannot be one and be reliable — the fallback mounts with its text already in place, which is
 * what ARIA22's "before the status message occurs" rules out. `Announce` routes the same sentence
 * to the region in `ForetagSokAnnouncer`, which holds its role before the message reaches it —
 * that ordering is the criterion, and it holds even where the region and this subtree mount in the
 * same commit. `aria-busy` stays: it describes THIS subtree's state, not an announcement.
 * The grey rows keep `aria-hidden` so nothing reads them as content.
 */

const SKELETON_ROWS = 8;

export function ForetagSokResultsSkeleton() {
  const t = useTranslations("pages.foretag.sok");
  return (
    <div aria-busy="true" className="mt-8">
      <Announce message={t("loadingResults")} />
      <p className="text-body-sm text-text-primary">{t("loadingResults")}</p>
      <div className="mt-6 flex flex-col gap-2" aria-hidden="true">
        {Array.from({ length: SKELETON_ROWS }, (_, i) => (
          <div key={i} className="jp-skeleton h-10 w-full" />
        ))}
      </div>
    </div>
  );
}
