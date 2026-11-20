import type { ReactNode } from "react";

/**
 * An admin page's h1, its optional lede and an optional aside at the right edge (a period group,
 * later a status line). The lede says what the page holds, never how to use it (DESIGN.md §8).
 */
export function AdminPageHeader({
  title,
  lede,
  aside,
  headingId,
}: {
  readonly title: string;
  readonly lede?: string;
  readonly aside?: ReactNode;
  /** Given where the h1 takes focus once the last row of a list has left it (DESIGN.md §6). */
  readonly headingId?: string;
}) {
  return (
    <div className="jp-adminhead">
      <div>
        <h1 id={headingId} tabIndex={headingId === undefined ? undefined : -1} className="jp-h1">
          {title}
        </h1>
        {lede === undefined ? null : <p className="jp-lede">{lede}</p>}
      </div>
      {aside === undefined ? null : <div className="jp-adminhead__aside">{aside}</div>}
    </div>
  );
}
