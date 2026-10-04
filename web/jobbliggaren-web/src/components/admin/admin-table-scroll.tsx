import type { ReactNode } from "react";

/**
 * A wide admin table's scroll region. At narrow widths the table scrolls inside it rather than
 * the page scrolling sideways (WCAG 1.4.10); the region is focusable and named by the table's
 * caption, so a keyboard user can scroll it (WCAG 2.1.1) and the region's name never repeats the
 * name of the section around it.
 */
export function AdminTableScroll({
  labelledBy,
  children,
}: {
  readonly labelledBy: string;
  readonly children: ReactNode;
}) {
  return (
    <div className="jp-admintable-scroll" role="region" aria-labelledby={labelledBy} tabIndex={0}>
      {children}
    </div>
  );
}
