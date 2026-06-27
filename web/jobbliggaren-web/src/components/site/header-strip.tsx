import Link from "next/link";
import type { ReactNode } from "react";
import { BrandLogo } from "@/components/brand/brand-logo";

/**
 * HeaderStrip — the shared structural header skeleton for the logged-in shells
 * (app, guest, admin). LP-5b / #259, epic #267.
 *
 * It owns ONLY the chrome the three shells genuinely share: the sticky
 * `.jp-header` white strip (`role="banner"`, kept white in dark mode via the
 * scoped `[data-theme="dark"] .jp-header` token override in globals.css), the
 * `.jp-header__inner` flex row, and the left brand slot (`<BrandLogo>` linked
 * home). The variable, surface-specific content is COMPOSED in as `children`:
 *
 *   - app:   primary nav + `<HeaderStats>` (server-fed, polls) + actions/drawer
 *   - guest: guest nav + "Logga in"/"Registrera" CTAs
 *   - admin: `<AdminNav>` + account email + logout
 *
 * No `variant` discriminator: the three surfaces share STRUCTURE, not content,
 * and their content is genuinely disjoint — a `variant="full"` would be a false
 * single variant that has to branch app|guest|admin internally and would drag
 * each shell's client logic (drawer focus-trap, popovers, polling) into one file
 * (senior-cto-advisor bind #259, Option C — composition over configuration;
 * mirrors the #258 single-variant rejection). Composition keeps HeaderStrip
 * presentational and server-safe, so each shell's client surface stays its own.
 *
 * The minimal public header (`site-header.tsx`, `.jp-head`, theme-adaptive) is
 * deliberately NOT folded in here: it uses a different CSS contract and a
 * `<nav>`-as-inner structure, and merging the `.jp-head`/`.jp-header` namespaces
 * carries dark-mode regression risk across surfaces — a separate optional
 * follow-up, not a condition of this composition (CTO trade-off, #259).
 */
export function HeaderStrip({
  brandHref,
  brandLabel,
  children,
}: {
  brandHref: string;
  brandLabel: string;
  children: ReactNode;
}) {
  return (
    <header className="jp-header" role="banner">
      <div className="jp-header__inner">
        <Link href={brandHref} className="jp-brand" aria-label={brandLabel}>
          <BrandLogo />
        </Link>
        {children}
      </div>
    </header>
  );
}
