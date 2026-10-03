import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { ArrowLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getActivityReport } from "@/lib/api/applications";
import {
  SWEDISH_TIME_ZONE,
  lastTwelveSwedishMonths,
  withSelectedMonth,
} from "@/lib/time/swedish-calendar";
import { assertNever } from "@/lib/dto/_helpers";
import {
  ActivityReportView,
  type ActivityReportRow,
  type MonthOption,
} from "@/components/aktivitetsrapport/activity-report-view";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("aktivitetsrapport");
  return { title: t("meta.title") };
}

// Arbetsförmedlingen's "Mina sidor" — where you log in with BankID and file the
// activity report (verified live 2026-06-28; the previously-guessed
// /aktivitetsrapportera slug 404'd). AF surfaces activity reporting from Mina
// sidor (there is no public deep-link to the form itself). The CTA opens it in
// a new tab.
const AF_ACTIVITY_REPORT_URL =
  "https://arbetsformedlingen.se/for-arbetssokande/mina-sidor";

/** Parse a "YYYY-MM" search param to a valid (year, month) pair, else null. */
function parseMonthParam(raw: string | undefined): { year: number; month: number } | null {
  if (!raw) return null;
  const match = /^(\d{4})-(\d{2})$/.exec(raw);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  if (month < 1 || month > 12 || year < 2000 || year > 2100) return null;
  return { year, month };
}

function pad2(value: number): string {
  return String(value).padStart(2, "0");
}

export default async function AktivitetsrapportPage({
  searchParams,
}: {
  searchParams: Promise<{ [key: string]: string | string[] | undefined }>;
}) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("aktivitetsrapport");
  const format = await getFormatter();

  const { month: monthParam } = await searchParams;
  const parsed = parseMonthParam(
    typeof monthParam === "string" ? monthParam : undefined,
  );

  const result = await getActivityReport(parsed?.year, parsed?.month);
  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return (
        <ErrorShell title={t("error.title")} body={t("error.rateLimited")} />
      );
    case "notFound":
    case "forbidden":
    case "error":
      return <ErrorShell title={t("error.title")} body={t("error.body")} />;
    default:
      return assertNever(result);
  }

  const report = result.data;

  // The backend echoes the resolved month (it defaults to the CURRENT month, on
  // the Swedish civil calendar) — this is the source of truth for the picker
  // value. Klas ruled 2026-07-29 that the code is right and the several places
  // still documenting a "previous month" default are the defect; they were
  // corrected in #1141.
  const selectedMonth = `${report.year}-${pad2(report.month)}`;
  const monthLabel = formatMonthLabel(format, report.year, report.month);
  const monthOptions = buildMonthOptions(format, report.year, report.month);

  // "Datum sökt" is rendered AND copied as a locale-independent YYYY-MM-DD in
  // Europe/Stockholm — the form-ready value for Arbetsförmedlingen, and the
  // calendar date the person actually applied (regardless of UI language).
  const stockholmDate = new Intl.DateTimeFormat("sv-SE", {
    timeZone: SWEDISH_TIME_ZONE,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });

  const rows: ActivityReportRow[] = report.applications.map((item) => ({
    applicationId: item.applicationId,
    appliedDate: stockholmDate.format(new Date(item.appliedAt)),
    employer: item.employer ?? null,
    title: item.title ?? null,
    location: item.location ?? null,
    source: item.source ?? null,
    url: item.url ?? null,
    // #892: strukturell borttagen-signal (aldrig literal-match — "[raderad]"
    // når inte wiren; CTO R5).
    adRemoved: item.adStatus === "Erased",
  }));

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("title")}</h1>
            <p className="jp-pagehero__lede">{t("lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <Link
          href="/ansokningar"
          className="jp-backlink mb-4"
        >
          <ArrowLeft size={16} aria-hidden="true" />
          {t("back")}
        </Link>

        <ActivityReportView
          rows={rows}
          selectedMonth={selectedMonth}
          monthLabel={monthLabel}
          monthOptions={monthOptions}
          afUrl={AF_ACTIVITY_REPORT_URL}
        />
      </div>
    </>
  );
}

function ErrorShell({ title, body }: { title: string; body: string }) {
  return (
    <div className="jp-container jp-page">
      <div className="jp-page__title-block">
        <h1 className="jp-page__title">{title}</h1>
        <p className="jp-page__lede">{body}</p>
      </div>
    </div>
  );
}

type Formatter = Awaited<ReturnType<typeof getFormatter>>;

function formatMonthLabel(format: Formatter, year: number, month: number): string {
  // The zone argument is a deliberate restatement of a global invariant, not a
  // fix: `src/i18n/request.ts` already pins Europe/Stockholm for every
  // `format.dateTime`, so the failure this guards against — a midnight-UTC
  // carrier formatted in a zone BEHIND UTC naming the previous month, the way
  // Date.UTC(2026, 0, 1) is 19:00 on 31 December in New York — has never been
  // reachable here. Named at the call site so it survives the global pin being
  // removed, and the noon carrier makes it robust for zones ±12 h either way.
  //
  // Contrast lib/i18n/format.ts, which deliberately never names the zone because
  // the configuration owns it, and the raw `Intl.DateTimeFormat` in
  // lib/time/swedish-calendar.ts, which MUST name it — there is no configuration
  // to inherit. (By symbol, not line: this repo has twice had a cross-file line
  // citation go stale inside a single PR.)
  return format.dateTime(new Date(Date.UTC(year, month - 1, 1, 12)), {
    timeZone: SWEDISH_TIME_ZONE,
    month: "long",
    year: "numeric",
  });
}

/**
 * The picker's options, newest first: the last twelve Swedish civil months, plus
 * the selected month when a deep link points outside that window.
 *
 * Every decision here lives in `lib/time/swedish-calendar.ts` and is tested
 * there. This function is the formatting shell, deliberately — when the anchor
 * was inline, reverting it to `getUTCMonth()` survived the whole suite.
 */
function buildMonthOptions(
  format: Formatter,
  selectedYear: number,
  selectedMonth: number,
): MonthOption[] {
  const months = withSelectedMonth(lastTwelveSwedishMonths(new Date()), {
    year: selectedYear,
    month: selectedMonth,
  });
  return months.map((m) => ({
    value: `${m.year}-${pad2(m.month)}`,
    label: formatMonthLabel(format, m.year, m.month),
  }));
}
