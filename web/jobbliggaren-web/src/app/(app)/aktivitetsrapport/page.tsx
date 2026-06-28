import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { ArrowLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getActivityReport } from "@/lib/api/applications";
import { assertNever } from "@/lib/dto/_helpers";
import {
  ActivityReportView,
  type ActivityReportRow,
  type MonthOption,
} from "@/components/aktivitetsrapport/activity-report-view";

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

  // The backend echoes the resolved month (it defaults to the previous month
  // when none is given) — this is the source of truth for the picker value.
  const selectedMonth = `${report.year}-${pad2(report.month)}`;
  const monthLabel = formatMonthLabel(format, report.year, report.month);
  const monthOptions = buildMonthOptions(format, report.year, report.month);

  // "Datum sökt" is rendered AND copied as a locale-independent YYYY-MM-DD in
  // Europe/Stockholm — the form-ready value for Arbetsförmedlingen, and the
  // calendar date the person actually applied (regardless of UI language).
  const stockholmDate = new Intl.DateTimeFormat("sv-SE", {
    timeZone: "Europe/Stockholm",
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
          className="mb-4 inline-flex items-center gap-1.5 text-text-primary hover:underline"
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
  return format.dateTime(new Date(Date.UTC(year, month - 1, 1)), {
    month: "long",
    year: "numeric",
  });
}

/**
 * The last 12 months (newest first), guaranteed to include the resolved month
 * so the picker value always matches an option even when the user navigates to
 * an older month by URL.
 */
function buildMonthOptions(
  format: Formatter,
  selectedYear: number,
  selectedMonth: number,
): MonthOption[] {
  const now = new Date();
  const months: { year: number; month: number }[] = [];
  for (let i = 0; i < 12; i++) {
    const d = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - i, 1));
    months.push({ year: d.getUTCFullYear(), month: d.getUTCMonth() + 1 });
  }
  if (!months.some((m) => m.year === selectedYear && m.month === selectedMonth)) {
    months.push({ year: selectedYear, month: selectedMonth });
    months.sort((a, b) => b.year - a.year || b.month - a.month);
  }
  return months.map((m) => ({
    value: `${m.year}-${pad2(m.month)}`,
    label: formatMonthLabel(format, m.year, m.month),
  }));
}
