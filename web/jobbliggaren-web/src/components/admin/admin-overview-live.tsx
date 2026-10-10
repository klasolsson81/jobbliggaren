"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { STANDALONE_LINK } from "@/components/auth/mail-link";
import { useTranslations } from "next-intl";
import { overviewSnapshotSchema, type AdminOverviewSnapshot } from "@/lib/dto/admin-overview";
import { OVERVIEW_REFRESH_MS, retainOverview } from "@/lib/admin/overview";
import { AdminOverviewAnnouncements } from "./admin-overview-announcements";
import { AdminOverview } from "./admin-overview";

const ALL_FAILED: AdminOverviewSnapshot = {
  accounts: { kind: "failed" }, audit: { kind: "failed" }, jobs: { kind: "failed" }, backup: { kind: "failed" },
};

type State =
  | { readonly kind: "loaded"; readonly data: AdminOverviewSnapshot }
  | { readonly kind: "unauthorized" | "forbidden" };

export function AdminOverviewLive({ initial, initialNow }: {
  readonly initial: AdminOverviewSnapshot;
  readonly initialNow: number;
}) {
  const t = useTranslations("admin.users.errors");
  const [state, setState] = useState<State>({ kind: "loaded", data: initial });
  const [now, setNow] = useState(initialNow);
  const allowed = state.kind === "loaded";

  useEffect(() => {
    if (!allowed) return;
    let active: AbortController | null = null;
    let stopped = false;
    async function refresh() {
      if (stopped || document.hidden || active !== null) return;
      const controller = new AbortController();
      active = controller;
      setNow(Date.now());
      try {
        const response = await fetch("/api/admin/oversikt", { cache: "no-store", signal: controller.signal });
        if (controller.signal.aborted || stopped) return;
        if (response.status === 401 || response.status === 403) {
          stopped = true;
          setState({ kind: response.status === 401 ? "unauthorized" : "forbidden" });
          return;
        }
        const parsed = response.ok ? overviewSnapshotSchema.safeParse(await response.json()) : null;
        if (controller.signal.aborted || stopped) return;
        const next: AdminOverviewSnapshot = parsed?.success ? parsed.data : ALL_FAILED;
        setState((current) => current.kind === "loaded"
          ? { kind: "loaded", data: retainOverview(current.data, next) }
          : current);
      } catch {
        if (!controller.signal.aborted && !stopped) {
          setState((current) => current.kind === "loaded"
            ? { kind: "loaded", data: retainOverview(current.data, ALL_FAILED) }
            : current);
        }
      } finally {
        if (active === controller) active = null;
        if (!controller.signal.aborted && !stopped) setNow(Date.now());
      }
    }
    function visibilityChanged() {
      if (document.hidden) {
        active?.abort();
        active = null;
      } else {
        void refresh();
      }
    }
    const timer = setInterval(() => { if (!document.hidden) void refresh(); }, OVERVIEW_REFRESH_MS);
    document.addEventListener("visibilitychange", visibilityChanged);
    return () => {
      stopped = true;
      clearInterval(timer);
      document.removeEventListener("visibilitychange", visibilityChanged);
      active?.abort();
    };
  }, [allowed]);

  if (state.kind !== "loaded") {
    return <div role="alert">
      <p>{t(state.kind)}</p>
      {state.kind === "unauthorized" ? <Link href="/logga-in" className={STANDALONE_LINK}>{t("signIn")}</Link> : null}
    </div>;
  }
  return (
    <>
      <AdminOverviewAnnouncements observations={state.data} now={now} />
      <AdminOverview observations={state.data} now={now} />
    </>
  );
}