"use client";

import type { ReactNode } from "react";
import { usePathname } from "next/navigation";
import { PUBLIC_CATALOGUE_PATH } from "@/lib/job-ads/public-catalogue-url";

/**
 * The public header's one navigation link, "Lediga jobb" (ADR 0176 F14). A client island only to read the pathname:
 * `aria-current="page"` holds when the path is exactly `/lediga-jobb`, never as a prefix, so a page added below it later
 * is not marked as this one (stage-A constraint 2). Every catalogue page is the current page; the query is not part of
 * the path. The label arrives as children from the server header, so this island reads no messages.
 *
 * It never prefetches: the catalogue is read per request, and the header is on every public page.
 */
export function HeaderCatalogueLink({ children }: { children: ReactNode }) {
  const pathname = usePathname();

  return (
    <a
      href={PUBLIC_CATALOGUE_PATH}
      className="jp-nav__link"
      aria-current={pathname === PUBLIC_CATALOGUE_PATH ? "page" : undefined}
    >
      {children}
    </a>
  );
}
