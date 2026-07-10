/**
 * Skeleton for the `(app)` pages that use a plain `jp-h1` + `jp-lede` header
 * (NOT the `jp-pagehero` gradient band): /matchningar, /sparade, /sokningar,
 * /installningar, /ny-ansokan (#739 — finding
 * `p1-no-loading-tsx-any-primary-route`, P0).
 *
 * These pages must NOT fall back to the group `(app)/loading.tsx` pagehero
 * skeleton: a page with no band would flash a dark-green band that then vanishes
 * on swap (a self-inflicted CLS jump — the opposite of the audit's goal). So they
 * get this plain-header skeleton instead. The group pagehero fallback is reserved
 * for the pages that actually use `jp-pagehero` (the go-forward standard).
 *
 * `contained` wraps in `.jp-container.jp-page` for pages that own their width
 * (V3-native routes, e.g. /ny-ansokan); the default renders bare so the app-shell
 * transitional container supplies the width (matchningar/sparade/sokningar/
 * installningar). sr-only `role="status"` announces; decorative shapes
 * `aria-hidden`. Sync RSC, flat-grey `.jp-skeleton`, no animation.
 */
export function PlainHeaderSkeleton({
  label,
  contained = false,
}: {
  label: string;
  contained?: boolean;
}) {
  const wrapperClass = contained
    ? "jp-container jp-page flex flex-col"
    : "flex flex-col";
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {label}
      </span>
      <div className={wrapperClass} aria-hidden="true">
        <div>
          <span className="jp-skeleton block h-9 w-56 max-w-full" />
          <span className="jp-skeleton mt-2 block h-5 w-96 max-w-full" />
        </div>
        <div className="mt-7 flex flex-col gap-4">
          {[0, 1, 2, 3].map((row) => (
            <span key={row} className="jp-skeleton block h-4 w-3/4 max-w-full" />
          ))}
        </div>
      </div>
    </>
  );
}
