import Link from "next/link";
import { ApplicationStatusBadge } from "./application-status-badge";
import type { ApplicationDto } from "@/lib/types/applications";

interface ApplicationCardProps {
  application: ApplicationDto;
}

export function ApplicationCard({ application }: ApplicationCardProps) {
  const updatedAt = new Date(application.updatedAt).toLocaleDateString("sv-SE");

  return (
    <Link
      href={`/ansokningar/${application.id}`}
      className="flex items-center justify-between rounded-md border border-border bg-card px-4 py-3 text-sm hover:bg-surface-secondary transition-colors"
    >
      <div className="flex items-center gap-3">
        <ApplicationStatusBadge status={application.status} />
        <span className="text-body text-text-secondary font-mono text-xs">
          {application.id.slice(0, 8)}
        </span>
      </div>
      <span className="text-body-sm text-text-tertiary">{updatedAt}</span>
    </Link>
  );
}
