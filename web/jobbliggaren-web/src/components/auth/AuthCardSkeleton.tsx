/**
 * Static placeholder for <AuthCard/> while the landing hero's Suspense boundary
 * resolves. The inner LoginForm/RegisterForm read `useSearchParams`, which
 * suspends during static generation (the same reason `next build` requires the
 * boundary); the card is above the fold, so the fallback mirrors the
 * `.jp-auth-tabcard` shape to avoid layout shift when the real card mounts.
 *
 * Flat neutral grey `.jp-skeleton` blocks, no pulse/shimmer/glow (civic-utility,
 * mirrors JobAdListSkeleton). `aria-hidden`: a brief decorative placeholder; the
 * real card carries the labelled APG tablist. Sync RSC (no interactivity).
 */
export function AuthCardSkeleton() {
  return (
    <div className="jp-auth-tabcard" aria-hidden="true">
      <div className="jp-auth-tabs">
        <div className="jp-auth-tab">
          <span className="jp-skeleton block h-4 w-24" />
        </div>
        <div className="jp-auth-tab">
          <span className="jp-skeleton block h-4 w-20" />
        </div>
      </div>
      <div className="jp-auth-panel">
        {[0, 1].map((row) => (
          <div key={row} className="flex flex-col gap-2">
            <span className="jp-skeleton block h-3 w-28" />
            <span className="jp-skeleton block h-[46px] w-full" />
          </div>
        ))}
        <span className="jp-skeleton mt-1 block h-[46px] w-full" />
      </div>
    </div>
  );
}
