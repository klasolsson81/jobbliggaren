import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import type { AdminHostReading, AdminServerReading } from "@/lib/admin/host-observation";
import { byteUnitFor, formatBinaryBytes } from "@/lib/admin/format-bytes";
import type { AdminValueRegion } from "@/lib/admin/view-models";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminRegionLine } from "./admin-region-line";
import { AdminUnknown } from "./admin-unknown";

/**
 * The Server card's body (#1982, ADR 0158): the host's CPU, memory and disk, each its own row and its own
 * state. A reading is a number with a meter, or an en-dash and one line saying why there is none; one failing
 * reading never blanks the others (ADR 0150 D2). The page never computes a percentage: the meter is the
 * backend's, so the card and the backend cannot disagree about a denominator.
 *
 * The region carries the time the host was last sampled and whether any reading is stale (`serverRegion`
 * builds it, aged to the page's clock); this body only shows them.
 */
export function AdminServerBody({ region }: { readonly region: AdminValueRegion<AdminServerReading> }) {
  const t = useTranslations("admin.overview.server");
  const format = useFormatter();
  const server = region.kind === "loaded" ? region.data : null;

  const percent = (value: number) =>
    format.number(value / 100, { style: "percent", minimumFractionDigits: 1, maximumFractionDigits: 1 });

  function meter(value: { readonly percent: number }, detail: string): ReactNode {
    return (
      <>
        <span className="jp-adminmeter">
          <span>{percent(value.percent)}</span>
          <span className="jp-adminmeter__track" aria-hidden="true">
            <span className="jp-adminmeter__fill" style={{ inlineSize: `${Math.min(100, Math.max(0, value.percent))}%` }} />
          </span>
        </span>
        <span className="jp-adminmeter__detail">{detail}</span>
      </>
    );
  }

  function missing(reading: AdminHostReading<unknown> | undefined): ReactNode {
    return (
      <>
        <AdminUnknown />
        {reading && reading.kind !== "value" ? (
          <span className="jp-adminmeter__detail">{t(`state.${reading.kind}`)}</span>
        ) : null}
      </>
    );
  }

  const cpu = server?.cpu;
  const memory = server?.memory;
  const disk = server?.disk;
  const memoryUnit = memory?.kind === "value" ? byteUnitFor(memory.value.totalBytes) : "B";
  const diskUnit = disk?.kind === "value" ? byteUnitFor(disk.value.totalBytes) : "B";
  const inUnit = (bytes: number, unit: typeof memoryUnit) => formatBinaryBytes(format, bytes, unit).value;

  return (
    <div className="jp-admincard__body">
      <dl className="jp-admindl">
        <Row label={t("cpu")}>
          {cpu?.kind === "value" ? meter(cpu.value, t("cpuWindow", { seconds: cpu.value.windowSeconds })) : missing(cpu)}
        </Row>
        <Row label={t("memory")}>
          {memory?.kind === "value"
            ? meter(memory.value, t("memoryUsed", {
                used: inUnit(memory.value.usedBytes, memoryUnit),
                total: inUnit(memory.value.totalBytes, memoryUnit),
                unit: memoryUnit,
              }))
            : missing(memory)}
        </Row>
        <Row label={t("disk")}>
          {disk?.kind === "value"
            ? meter(disk.value, t("diskFree", {
                free: inUnit(disk.value.freeBytes, diskUnit),
                total: inUnit(disk.value.totalBytes, diskUnit),
                unit: diskUnit,
              }))
            : missing(disk)}
        </Row>
      </dl>
      {region.kind !== "loaded" ? (
        <AdminRegionLine quiet kind={region.kind} />
      ) : region.sampledAt ? (
        <p className="jp-adminkpi__sub">
          {t("sampledAt")} <time dateTime={region.sampledAt}>{formatDateTime(format, region.sampledAt)}</time>
          {region.stale && server ? (
            <>
              <br />
              {t("stale", { minutes: Math.round(server.staleAfterMs / 60_000) })}
            </>
          ) : null}
        </p>
      ) : null}
    </div>
  );
}

function Row({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </>
  );
}
