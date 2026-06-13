import { cn } from "@/lib/utils";

/**
 * Matchnings-progressbar (6px). Färgsätts efter värde:
 * ≥75 brand, 50–74 info, <50 warning. Värdet visas alltid som text
 * bredvid baren (aldrig enbart färg — a11y).
 */
export function MatchBar({
  value,
  className,
}: {
  value: number;
  className?: string;
}) {
  const clamped = Math.max(0, Math.min(100, Math.round(value)));
  const fillVariant =
    clamped >= 75 ? "" : clamped >= 50 ? "jp-match__fill--mid" : "jp-match__fill--low";

  return (
    <span
      className={cn("jp-match", className)}
      title={`Matchning ${clamped}%`}
    >
      <span className="jp-match__bar">
        <span
          className={cn("jp-match__fill", fillVariant)}
          style={{ width: `${clamped}%` }}
        />
      </span>
      <span>{clamped}%</span>
    </span>
  );
}
