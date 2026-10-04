import type { ReactNode } from "react";

/**
 * An admin page's h1, its optional lede and an optional aside at the right edge (a period group,
 * later a status line). The lede says what the page holds, never how to use it (DESIGN.md §8).
 */
export function AdminPageHeader({
  title,
  lede,
  aside,
}: {
  readonly title: string;
  readonly lede?: string;
  readonly aside?: ReactNode;
}) {
  return (
    <div className="jp-adminhead">
      <div>
        <h1 className="jp-h1">{title}</h1>
        {lede === undefined ? null : <p className="jp-lede">{lede}</p>}
      </div>
      {aside === undefined ? null : <div className="jp-adminhead__aside">{aside}</div>}
    </div>
  );
}
