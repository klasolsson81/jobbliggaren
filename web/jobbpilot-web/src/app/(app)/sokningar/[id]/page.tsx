import Link from "next/link";
import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getSavedSearch, runSavedSearch } from "@/lib/api/saved-searches";
import { assertNever } from "@/lib/dto/_helpers";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";

type RunSearchParams = { page?: string; pageSize?: string };

interface PageProps {
  // Next.js 16 App Router: params + searchParams är Promise.
  params: Promise<{ id: string }>;
  searchParams: Promise<RunSearchParams>;
}

const DEFAULT_PAGE_SIZE = 20;

export default async function KorSparadSokningPage({
  params,
  searchParams,
}: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const { id } = await params;
  const sp = await searchParams;
  const page = parsePositiveInt(sp.page, 1);
  const pageSize = Math.min(
    parsePositiveInt(sp.pageSize, DEFAULT_PAGE_SIZE),
    100
  );

  const [meta, result] = await Promise.all([
    getSavedSearch(id),
    runSavedSearch(id, page, pageSize),
  ]);

  const title =
    meta.kind === "ok" ? meta.data.name : "Sparad sökning";

  return (
    <div className="flex flex-col">
      <div className="flex items-center gap-3">
        <Link
          href="/sokningar"
          className="text-body-sm text-text-secondary hover:text-text-primary"
        >
          Sparade sökningar
        </Link>
        <span className="text-text-tertiary">/</span>
        <span className="text-body-sm text-text-secondary font-mono">
          {id.slice(0, 8)}
        </span>
      </div>

      <div>
        <h1 className="jp-h1 mt-1">{title}</h1>
        <p className="jp-lede">Resultat för den sparade sökningen.</p>
      </div>

      <div className="mt-6 flex flex-col gap-2.5">
        {renderResult(result, id)}
      </div>
    </div>
  );
}

function renderResult(
  result: Awaited<ReturnType<typeof runSavedSearch>>,
  id: string
) {
  switch (result.kind) {
    case "ok":
      return (
        <>
          <p
            className="font-mono text-body-sm text-text-secondary"
            role="status"
            aria-live="polite"
          >
            {result.data.totalCount === 0
              ? "Inga träffar"
              : `${result.data.totalCount.toLocaleString("sv-SE")} träffar`}
          </p>
          <JobAdList jobAds={result.data.items} />
          <JobAdPagination
            page={result.data.page}
            pageSize={result.data.pageSize}
            totalCount={result.data.totalCount}
            buildHref={(targetPage) => buildPageHref(id, targetPage)}
          />
        </>
      );
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return (
        <div
          role="alert"
          className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4"
        >
          <p className="text-body font-medium text-warning-700">
            För många förfrågningar
          </p>
          <p className="mt-1 text-body-sm text-warning-700">
            Du har gjort för många förfrågningar på kort tid. Försök igen om{" "}
            {result.retryAfterSeconds} sekunder.
          </p>
        </div>
      );
    case "notFound":
      return (
        <div className="rounded-md border border-border-default px-6 py-4">
          <p className="text-body font-medium text-text-primary">
            Sökningen hittades inte
          </p>
          <p className="mt-1 text-body-sm text-text-secondary">
            Den kan ha tagits bort.{" "}
            <Link
              href="/sokningar"
              className="underline underline-offset-2 hover:text-text-primary"
            >
              Tillbaka till sparade sökningar
            </Link>
            .
          </p>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
          <p className="text-body font-medium">Kunde inte köra sökningen</p>
          <p className="mt-1 text-body-sm">
            Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.
          </p>
        </div>
      );
    default:
      return assertNever(result);
  }
}

function parsePositiveInt(raw: string | undefined, fallback: number): number {
  if (!raw) return fallback;
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) && n > 0 ? n : fallback;
}

function buildPageHref(id: string, targetPage: number): string {
  const url = new URLSearchParams();
  if (targetPage !== 1) url.set("page", String(targetPage));
  const qs = url.toString();
  return qs.length > 0 ? `/sokningar/${id}?${qs}` : `/sokningar/${id}`;
}
