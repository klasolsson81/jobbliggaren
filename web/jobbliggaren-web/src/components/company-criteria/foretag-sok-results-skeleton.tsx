import { useTranslations } from "next-intl";

/**
 * #560 PR-B — the loading state for the `/foretag/sok` results region. Rendered as the `<Suspense>`
 * fallback around the results Server Component (and by the route `loading.tsx`) — ONLY the results
 * swap to the skeleton while a search runs; the pagehero + filter panel + org.nr island stay rendered.
 * Civic-utility: flat neutral grey rows (`.jp-skeleton`), no shimmer, no pulse, no spinner.
 *
 * a11y: `role="status"` + `aria-live="polite"` announces the single visible "Söker företag…" sentence;
 * the grey rows are `aria-hidden` so the announcement is a short sentence, not empty decoration.
 */

const SKELETON_ROWS = 8;

export function ForetagSokResultsSkeleton() {
  const t = useTranslations("pages.foretag.sok");
  return (
    <div role="status" aria-live="polite" aria-busy="true" className="mt-8">
      <p className="text-body-sm text-text-primary">{t("loadingResults")}</p>
      <div className="mt-6 flex flex-col gap-2" aria-hidden="true">
        {Array.from({ length: SKELETON_ROWS }, (_, i) => (
          <div key={i} className="jp-skeleton h-10 w-full" />
        ))}
      </div>
    </div>
  );
}
