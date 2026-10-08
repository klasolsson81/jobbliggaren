import type { ReactNode } from "react";
import { useTranslations } from "next-intl";
import { useFormatter } from "next-intl";
import { formatDateTime } from "@/lib/i18n/format";
import { StatusDot, type StatusTone } from "@/components/ui/status-dot";
import type {
  AdminAccountRole,
  AdminAccountRow,
  AdminAccountStatus as Status,
} from "@/lib/admin/view-models";

const TONE: Readonly<Record<Status, StatusTone>> = {
  active: "success",
  pendingDeletion: "neutral",
  profileMissing: "warning",
  suspended: "danger",
};

/** An account's lifecycle state as a dot and a label; the label carries it, the colour only repeats it. */
export function AdminAccountStatus({ status }: Pick<AdminAccountRow, "status">) {
  const t = useTranslations("admin.users.status");
  return <StatusDot tone={TONE[status]}>{t(status)}</StatusDot>;
}

/**
 * The ledger's status cell: the state, then the facts that can go with any state as plain lines
 * below it, outside the dot (ADR 0151). A deletion without a date gets no line.
 */
export function AdminAccountStatusCell({
  status,
  deletionEarliest,
  deletion,
  emailConfirmed,
}: Pick<AdminAccountRow, "status" | "deletionEarliest" | "deletion" | "emailConfirmed">) {
  const t = useTranslations("admin.users.statusLine");
  const unavailable = useTranslations("admin.unavailable");
  const format = useFormatter();
  return (
    <div className="jp-adminusers__status">
      <AdminAccountStatus status={status} />
      {status === "pendingDeletion" && deletion != null ? (
        <span className="jp-adminusers__statusline">
          {t.rich("deletionScheduled", { date: formatDateTime(format, deletion.scheduledRunAt) ?? unavailable("unknownValue"), nowrap: unbroken })}
        </span>
      ) : status === "pendingDeletion" && deletionEarliest !== null ? (
        <span className="jp-adminusers__statusline">
          {t.rich("deletionEarliest", { date: deletionEarliest, nowrap: unbroken })}
        </span>
      ) : null}
      {emailConfirmed ? null : <span className="jp-adminusers__statusline">{t("emailUnconfirmed")}</span>}
    </div>
  );
}

/** A date that may move to the next line but never breaks inside itself. */
export function unbroken(chunks: ReactNode) {
  return <span className="jp-adminusers__date">{chunks}</span>;
}

export function AdminRolePill({ role }: { readonly role: AdminAccountRole }) {
  const t = useTranslations("admin.users.role");
  return (
    <span className={role === "admin" ? "jp-pill jp-pill--brand" : "jp-pill jp-pill--neutral"}>
      {t(role)}
    </span>
  );
}
