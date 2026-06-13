import Link from "next/link";
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
  return (
    <form
      method="get"
      action="/admin/granskning"
      className="grid gap-4 rounded-md border border-border bg-surface-secondary p-4 sm:grid-cols-2 lg:grid-cols-5"
      aria-label="Filtrera granskningsloggen"
    >
      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-from">Från</Label>
        <Input
          id="filter-from"
          name="from"
          type="datetime-local"
          defaultValue={toLocalInput(current.from)}
        />
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-to">Till</Label>
        <Input
          id="filter-to"
          name="to"
          type="datetime-local"
          defaultValue={toLocalInput(current.to)}
        />
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-event-type">Händelse</Label>
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
          className="text-body-sm text-text-secondary"
        >
          Format: Aggregat.Händelse, t.ex. Application.Created
        </p>
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-aggregate-type">Aggregat</Label>
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
          className="text-body-sm text-text-secondary"
        >
          Aggregatnamn, t.ex. Application
        </p>
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="filter-user-id">Användar-ID</Label>
        <Input
          id="filter-user-id"
          name="userId"
          type="text"
          defaultValue={current.userId ?? ""}
          aria-describedby="filter-user-id-hint"
        />
        <p
          id="filter-user-id-hint"
          className="text-body-sm text-text-secondary"
        >
          Anges som UUID
        </p>
      </div>

      <div className="flex items-end gap-2 sm:col-span-2 lg:col-span-5">
        <Button type="submit" size="sm">
          Använd filter
        </Button>
        <Button asChild variant="ghost" size="sm">
          <Link href="/admin/granskning">Rensa</Link>
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
