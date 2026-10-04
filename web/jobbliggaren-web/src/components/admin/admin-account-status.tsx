import { useTranslations } from "next-intl";
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
  emailConfirmed,
}: Pick<AdminAccountRow, "status" | "deletionEarliest" | "emailConfirmed">) {
  const t = useTranslations("admin.users.statusLine");
  return (
    <div className="jp-adminusers__status">
      <AdminAccountStatus status={status} />
      {status === "pendingDeletion" && deletionEarliest !== null ? (
        <span className="jp-adminusers__statusline">{t("deletionEarliest", { date: deletionEarliest })}</span>
      ) : null}
      {emailConfirmed ? null : <span className="jp-adminusers__statusline">{t("emailUnconfirmed")}</span>}
    </div>
  );
}

export function AdminRolePill({ role }: { readonly role: AdminAccountRole }) {
  const t = useTranslations("admin.users.role");
  return (
    <span className={role === "admin" ? "jp-pill jp-pill--brand" : "jp-pill jp-pill--neutral"}>
      {t(role)}
    </span>
  );
}
