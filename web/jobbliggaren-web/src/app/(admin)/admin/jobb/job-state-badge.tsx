import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";

// Civic status badge for a recurring job's last Hangfire state (TD-83 / #204).
// Synchronous next-intl translator keeps this a non-async RSC. Not color-only:
// the localized text label always carries the meaning (WCAG 1.4.1), the
// semantic token only reinforces it. No `role="status"` — on a 15-row table
// that would announce 15 polite live-regions on render (mirrors the
// job-ad-status-badge decision).

type BadgeVariant = "Success" | "Danger" | "Info" | "Neutral";

const VARIANT_CLASSES: Record<BadgeVariant, string> = {
  Success: "bg-success-50 text-success-700",
  Danger: "bg-danger-50 text-danger-700",
  Info: "bg-info-50 text-info-700",
  Neutral: "bg-surface-tertiary text-text-secondary",
};

/**
 * Maps a raw Hangfire state name to a civic semantic variant. Unknown or null
 * states fall to Neutral ("aldrig körd" / unseen state) — truthful, never
 * fabricating a success/failure signal we do not have.
 */
function variantForState(state: string | null): BadgeVariant {
  switch (state) {
    case "Succeeded":
      return "Success";
    case "Failed":
      return "Danger";
    case "Processing":
      return "Info";
    default:
      return "Neutral";
  }
}

type StateLabelKey =
  | "states.succeeded"
  | "states.failed"
  | "states.processing"
  | "states.neverRun";

/**
 * Resolves the localized label key for a state. Keeps the mapping truthful:
 * each known Hangfire state has a Swedish/civic-English label; an unknown or
 * null state renders the "never run" label rather than echoing a raw English
 * verb the reader did not ask for. The template-literal return type lets
 * next-intl's typed `t` accept the computed key (mirrors lib/job-ads/status).
 */
function labelKeyForState(state: string | null): StateLabelKey {
  switch (state) {
    case "Succeeded":
      return "states.succeeded";
    case "Failed":
      return "states.failed";
    case "Processing":
      return "states.processing";
    default:
      return "states.neverRun";
  }
}

interface JobStateBadgeProps {
  state: string | null;
  className?: string;
}

export function JobStateBadge({ state, className }: JobStateBadgeProps) {
  const t = useTranslations("admin.jobb");
  const variant = variantForState(state);
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-pill px-2 py-0.5 text-micro leading-4 font-medium",
        VARIANT_CLASSES[variant],
        className,
      )}
    >
      {t(labelKeyForState(state))}
    </span>
  );
}
