const STEP_ROWS = [0, 1, 2, 3, 4, 5, 6] as const;
const PARK_BUTTONS = [0, 1, 2] as const;
const SECTIONS = [0, 1] as const;

/**
 * The loading state of the full page `/ansokningar/[id]`, in the shape of the one detail body
 * (#1827 M6): the back link, the title and subtitle, the status block with its left edge, the
 * primary CTA, the seven steps, the park row, two sections and the foot. It reuses the page's
 * envelope and the body's own classes.
 *
 * Announcement: an sr-only `role="status"` reads the passed Swedish label; the blocks are
 * `aria-hidden`. Sync RSC, flat `.jp-skeleton` blocks, no animation, no id (safe to render
 * beside the real page mid-swap). `label` is pre-translated by the caller.
 */
export function ApplicationDetailSkeleton({ label }: { label: string }) {
  return (
    <div className="jp-container jp-page">
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {label}
      </span>
      <div aria-hidden="true">
        <span className="jp-skeleton block h-9 w-56 max-w-full [@media(max-width:768px)]:h-11" />
        <div className="jp-modal jp-modal--page">
          <header className="jp-modal__head">
            <div style={{ flex: 1 }}>
              <span className="jp-skeleton block h-6 w-72 max-w-full" />
              <span className="jp-skeleton mt-2 block h-4 w-40 max-w-full" />
            </div>
          </header>
          <div className="jp-modal__body">
            <div className="jp-modal__match jp-status-block">
              <span className="jp-skeleton block h-3 w-16" />
              <span className="jp-skeleton mt-2 block h-5 w-28" />
              <span className="jp-skeleton mt-2 block h-4 w-64 max-w-full" />
            </div>
            <div className="jp-drawer-actions">
              <span className="jp-skeleton block h-11 w-full" />
              <div>
                <span className="jp-skeleton mb-2.5 block h-3 w-40" />
                <div className="flex flex-col gap-0.5">
                  {STEP_ROWS.map((row) => (
                    <div
                      key={row}
                      className="flex h-9 items-center gap-2.5 px-2 [@media(max-width:768px)]:h-11"
                    >
                      <span className="jp-skeleton block size-6 shrink-0" />
                      <span className="jp-skeleton block h-4 w-32" />
                    </div>
                  ))}
                </div>
              </div>
              <div>
                <span className="jp-skeleton mb-2.5 block h-3 w-44" />
                <div className="flex gap-2">
                  {PARK_BUTTONS.map((button) => (
                    <span
                      key={button}
                      className="jp-skeleton block h-9 flex-1 [@media(max-width:768px)]:h-11"
                    />
                  ))}
                </div>
              </div>
            </div>
            {SECTIONS.map((section) => (
              <div key={section}>
                <span className="jp-skeleton mb-2.5 block h-3 w-32" />
                <span className="jp-skeleton block h-11 w-full" />
              </div>
            ))}
          </div>
          <div className="jp-modal__foot">
            <span className="jp-modal__foot__spacer" />
            <span className="jp-skeleton block h-9 w-36" />
            <span className="jp-skeleton block h-11 w-28" />
          </div>
        </div>
      </div>
    </div>
  );
}
