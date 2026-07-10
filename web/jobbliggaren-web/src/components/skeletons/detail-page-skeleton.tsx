/**
 * Skeleton for the full-page detail routes `/jobb/[id]` and `/ansokningar/[id]`
 * (#739 — the loading half of finding `g1-jobad-detail-open-serial-stages`, and
 * the P0 dead-click fix for the two full-page detail segments).
 *
 * Both detail pages render the same envelope — a `.jp-container.jp-page` holding
 * a non-floating `.jp-modal` (maxWidth 760, no shadow/animation) with
 * `__head` / `__body` / `__foot`. Re-using that envelope's exact classes and the
 * inline width style keeps the shape stable so real content does not shift in.
 * (The modal *intercept* keeps its `ModalLoadingShell` spinner — a formless
 * overlay wait; a full page has a known shape, so it gets a skeleton per the
 * spinner-vs-skeleton doctrine.)
 *
 * Announcement: an sr-only `role="status"` reads the passed Swedish label; the
 * visual envelope is `aria-hidden`. Sync RSC, flat grey `.jp-skeleton` blocks,
 * no animation. `label` is pre-translated by the caller so this stays i18n-free.
 */
export function DetailPageSkeleton({ label }: { label: string }) {
  return (
    <div className="jp-container jp-page">
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {label}
      </span>
      <div
        className="jp-modal"
        aria-hidden="true"
        style={{
          width: "100%",
          maxWidth: 760,
          maxHeight: "none",
          marginInline: "auto",
          boxShadow: "none",
          animation: "none",
        }}
      >
        <header className="jp-modal__head">
          <div style={{ flex: 1 }}>
            <span className="jp-skeleton block h-6 w-72 max-w-full" />
            <span className="jp-skeleton mt-2 block h-4 w-40 max-w-full" />
          </div>
          <span className="jp-skeleton block h-6 w-24" />
        </header>
        <div className="jp-modal__body">
          <div className="jp-modal__metarow">
            {[0, 1, 2].map((item) => (
              <div key={item} className="jp-modal__metaitem">
                <span className="jp-skeleton block h-3 w-20" />
                <span className="jp-skeleton mt-1 block h-4 w-28" />
              </div>
            ))}
          </div>
          <div className="mt-5 flex flex-col gap-2.5">
            <span className="jp-skeleton block h-3 w-32" />
            {[0, 1, 2, 3, 4].map((line) => (
              <span
                key={line}
                className={`jp-skeleton block h-4 ${line === 4 ? "w-2/3" : "w-full"}`}
              />
            ))}
          </div>
        </div>
        <div className="jp-modal__foot">
          <span className="jp-modal__foot__spacer" />
          <span className="jp-skeleton block h-10 w-28" />
          <span className="jp-skeleton block h-10 w-32" />
        </div>
      </div>
    </div>
  );
}
