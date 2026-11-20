import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminJuniorJobsView } from "@/components/admin/admin-junior-jobs-view";
import { decideJuniorJobAdAction } from "@/lib/actions/admin-junior-job-ads";
import {
  JUNIOR_JOBS_ROUTE,
  parseJuniorJobsQuery,
  type AdminJuniorJobsPage,
  type JuniorJobsSearchParams,
} from "@/lib/admin/junior-jobs";
import type { AdminValueRegion } from "@/lib/admin/view-models";
import { listJuniorJobAds } from "@/lib/api/admin-junior-job-ads";
import { toJuniorJobsPage } from "@/lib/dto/admin-junior-job-ads";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.juniorJobs");
  return { title: t("meta.title") };
}

/**
 * `/admin/juniorjobb` — the junior queue (#2264, ADR 0187). The URL drives the page: `flik` picks the tab and `sida`
 * its page, and one read answers both with the count of every tab. The decision is a Server Action that reads the
 * Admin role again; it goes to the list as a reference, so the page itself stays a Server Component.
 */
export default async function AdminJuniorJobsPage({
  searchParams,
}: {
  searchParams: Promise<JuniorJobsSearchParams>;
}) {
  const t = await getTranslations("admin.juniorJobs");
  const query = parseJuniorJobsQuery(await searchParams);
  const list = await listJuniorJobAds(query.tab, query.page);

  const region: AdminValueRegion<AdminJuniorJobsPage> =
    list.kind === "ok" ? { kind: "loaded", data: toJuniorJobsPage(list.data) } : { kind: "failed" };

  // A refused read says why, since trying again later does not help it; any other failure takes the shared line.
  const failed =
    list.kind === "rateLimited"
      ? t("errors.rateLimited", { seconds: list.retryAfterSeconds })
      : list.kind === "unauthorized"
        ? t("errors.unauthorized")
        : list.kind === "forbidden"
          ? t("errors.forbidden")
          : undefined;

  return (
    <div className="flex flex-col gap-6">
      <AdminJuniorJobsView
        basePath={JUNIOR_JOBS_ROUTE}
        query={query}
        region={region}
        failed={failed}
        onDecide={decideJuniorJobAdAction}
      />
    </div>
  );
}
