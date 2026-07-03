import Link from "next/link";
import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";

interface AuditLogFilterProps {
  current: {
    from?: string;
    to?: string;
    userId?: string;
    eventType?: string;
    aggregateType?: string;
  };
}

/**
 * URL-searchParam-driven filter. Server Component utan client-state — formuläret
 * gör GET till samma sida med nya params. Native HTML behaviors (Enter submits,
 * tab-navigation, browser-back). Civic-utility: zero JS för core-flöde.
 */
export function AuditLogFilter({ current }: AuditLogFilterProps) {
  // Synchronous next-intl translator — håller AuditLogFilter en icke-async RSC.
  const t = useTranslations("admin");
  return (
    <form
      method="get"
      action="/admin/granskning"
      className="grid gap-4 rounded-md border border-border bg-surface-secondary p-4 sm:grid-cols-2 lg:grid-cols-5"
      aria-label={t("audit.filter.formLabel")}
    >
      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-from">{t("audit.filter.from")}</Label>
        <Input
          id="filter-from"
          name="from"
          type="datetime-local"
          defaultValue={toLocalInput(current.from)}
        />
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-to">{t("audit.filter.to")}</Label>
        <Input
          id="filter-to"
          name="to"
          type="datetime-local"
          defaultValue={toLocalInput(current.to)}
        />
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-event-type">{t("audit.filter.eventType")}</Label>
        <Input
          id="filter-event-type"
          name="eventType"
          type="text"
          defaultValue={current.eventType ?? ""}
          maxLength={100}
          aria-describedby="filter-event-type-hint"
        />
        <p
          id="filter-event-type-hint"
          className="text-body-sm text-text-primary"
        >
          {t("audit.filter.eventTypeHint")}
        </p>
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-aggregate-type">
          {t("audit.filter.aggregateType")}
        </Label>
        <Input
          id="filter-aggregate-type"
          name="aggregateType"
          type="text"
          defaultValue={current.aggregateType ?? ""}
          maxLength={100}
          aria-describedby="filter-aggregate-type-hint"
        />
        <p
          id="filter-aggregate-type-hint"
          className="text-body-sm text-text-primary"
        >
          {t("audit.filter.aggregateTypeHint")}
        </p>
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-user-id">{t("audit.filter.userId")}</Label>
        <Input
          id="filter-user-id"
          name="userId"
          type="text"
          defaultValue={current.userId ?? ""}
          aria-describedby="filter-user-id-hint"
        />
        <p
          id="filter-user-id-hint"
          className="text-body-sm text-text-primary"
        >
          {t("audit.filter.userIdHint")}
        </p>
      </div>

      <div className="flex items-end gap-2 sm:col-span-2 lg:col-span-5">
        <Button type="submit" size="sm">
          {t("audit.filter.apply")}
        </Button>
        <Button asChild variant="ghost" size="sm">
          <Link href="/admin/granskning">{t("audit.filter.clear")}</Link>
        </Button>
      </div>
    </form>
  );
}

function toLocalInput(iso?: string): string {
  // datetime-local input wants format "YYYY-MM-DDTHH:mm". ISO-strängar från
  // backend är UTC ("YYYY-MM-DDTHH:mm:ss.fffZ") — trunkera till minutdjup.
  if (!iso) return "";
  const m = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2})/.exec(iso);
  return m?.[1] ?? "";
}
