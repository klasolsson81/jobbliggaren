import Link from "next/link";
import { StatusDot, type StatusTone } from "@/components/ui/status-dot";
import {
  formatSvDate,
  getStatusLabel,
  STATUS_BADGE_VARIANT,
  type BadgeVariant,
} from "@/lib/applications/status";
import type { ApplicationDto } from "@/lib/types/applications";

interface ApplicationRowProps {
  application: ApplicationDto;
}

const DOT_TONE: Record<BadgeVariant, StatusTone> = {
  Info: "info",
  Brand: "brand",
  Success: "success",
  Warning: "warning",
  Danger: "danger",
  Neutral: "neutral",
};

/**
 * Ledger-rad i ansökningspipelinen (ersätter ApplicationCard — det var
 * aldrig ett kort, det är en rad). Primär identitet = jobtitel — företag;
 * fallback till mono-kort-id när ingen kopplad/manuell annons finns
 * (tillstånd 3, §7). Status som StatusDot (lägst visuell vikt i tät lista,
 * §8 Area 1) — aldrig fylld pill här.
 */
export function ApplicationRow({ application }: ApplicationRowProps) {
  const { jobAd } = application;
  const tone = DOT_TONE[STATUS_BADGE_VARIANT[application.status]] ?? "neutral";

  const hasIdentity = jobAd != null;
  const primary = hasIdentity
    ? `${jobAd.title} — ${jobAd.company}`
    : `Ansökan #${application.id.slice(0, 8)}`;

  const updatedAt = formatSvDate(application.updatedAt);
  const expiresAt = formatSvDate(jobAd?.expiresAt);

  return (
    <Link
      href={`/ansokningar/${application.id}`}
      className="flex flex-col gap-1 border-b border-border-default px-1 py-3 transition-colors duration-75 last:border-b-0 hover:bg-surface-tertiary"
    >
      <span
        className={
          hasIdentity
            ? "text-body font-semibold text-text-primary"
            : "font-mono text-body font-semibold text-text-primary"
        }
      >
        {primary}
      </span>
      <span className="flex flex-wrap items-center gap-x-3 gap-y-1 text-body-sm text-text-secondary">
        <StatusDot tone={tone}>{getStatusLabel(application.status)}</StatusDot>
        {updatedAt && (
          <>
            <span aria-hidden="true">·</span>
            <span>
              Uppdaterad{" "}
              <span className="font-mono">{updatedAt}</span>
            </span>
          </>
        )}
        {expiresAt && (
          <>
            <span aria-hidden="true">·</span>
            <span>
              Sök senast{" "}
              <span className="font-mono">{expiresAt}</span>
            </span>
          </>
        )}
      </span>
    </Link>
  );
}
