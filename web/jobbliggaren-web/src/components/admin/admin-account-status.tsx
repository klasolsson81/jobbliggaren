import { useFormatter, useTranslations } from "next-intl";
import { StatusDot, type StatusTone } from "@/components/ui/status-dot";
import { formatDate } from "@/lib/i18n/format";
import type {
  AdminAccountRole,
  AdminAccountRow,
  AdminAccountStatus as Status,
} from "@/lib/admin/view-models";

const TONE: Readonly<Record<Status, StatusTone>> = {
  active: "success",
  suspended: "danger",
  unverified: "warning",
  pendingDeletion: "neutral",
};

/** An account's status as a dot and a label; the label carries it, the colour only repeats it. */
export function AdminAccountStatus({
  status,
  deletionEarliest,
}: Pick<AdminAccountRow, "status" | "deletionEarliest">) {
  const t = useTranslations("admin.users.status");
  const unknown = useTranslations("admin.unavailable")("unknownValue");
  const format = useFormatter();

  return (
    <StatusDot tone={TONE[status]}>
      {status === "pendingDeletion"
        ? t("pendingDeletion", { date: formatDate(format, deletionEarliest) ?? unknown })
        : t(status)}
    </StatusDot>
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
