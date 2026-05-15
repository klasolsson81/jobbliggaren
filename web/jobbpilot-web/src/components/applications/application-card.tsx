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
      className="flex items-center justify-between border-b border-border-default px-3 py-3 text-sm transition-colors duration-75 last:border-b-0 hover:bg-surface-tertiary"
    >
      <div className="flex items-center gap-3">
        <ApplicationStatusBadge status={application.status} />
        <span className="font-mono text-[11.5px] text-text-secondary">
          {application.id.slice(0, 8)}
        </span>
      </div>
      <span className="font-mono text-[11.5px] text-text-secondary">
        {updatedAt}
      </span>
    </Link>
  );
}
