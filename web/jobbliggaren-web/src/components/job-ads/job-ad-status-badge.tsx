import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";
import {
  jobAdStatusLabel,
  JOB_AD_STATUS_BADGE_VARIANT,
  type BadgeVariant,
} from "@/lib/job-ads/status";
import type { JobAdStatus } from "@/lib/dto/job-ads";

const VARIANT_CLASSES: Record<BadgeVariant, string> = {
  Info:    "bg-info-50 text-info-700",
  Brand:   "bg-brand-50 text-brand-700",
  Success: "bg-success-50 text-success-700",
  Warning: "bg-warning-50 text-warning-700",
  Danger:  "bg-danger-50 text-danger-700",
  Neutral: "bg-surface-tertiary text-text-primary",
};

interface JobAdStatusBadgeProps {
  status: JobAdStatus;
  className?: string;
}

export function JobAdStatusBadge({ status, className }: JobAdStatusBadgeProps) {
  // Synchronous next-intl translator — keeps this a non-async RSC.
  const t = useTranslations("jobads.enums");
  const variant = JOB_AD_STATUS_BADGE_VARIANT[status];
  // Inget `role="status"` — badge är dekorativ label, inte live-region.
  // På list-sida med N badges skulle role=status ge N polite-announcements
  // vid initial render (design-reviewer F2-P10 Minor 1, code-reviewer M2).
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-pill px-2 py-0.5 text-micro leading-4 font-medium",
        VARIANT_CLASSES[variant],
        className
      )}
    >
      {jobAdStatusLabel(t, status)}
    </span>
  );
}
