import Link from "next/link";
import { buildGuestPipeline } from "@/lib/guest/mock-data";

// F-Pre Punkt 5 — Gäst-ansökningar-pipeline. Mockdata-driven, ingen
// "Ny ansökan"-knapp (muterande action — Klas-direktiv §F). Samma applications
// som /gast/oversikt så summorna är synkade per Klas-direktiv §E.
// Använder befintliga `.jp-section`-mönster (paritet med
// `(app)/ansokningar`).

export function GuestAnsokningarPage() {
  const groups = buildGuestPipeline();
  const total = groups.reduce((sum, g) => sum + g.count, 0);

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <div className="jp-pagehero__kicker">Demopipeline</div>
            <h1 className="jp-pagehero__title">Mina ansökningar</h1>
            <p className="jp-pagehero__lede">
              {total} exempelansökningar fördelade över alla statuslägen.
            </p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {groups.map((group) => (
          <section
            key={group.status}
            className="jp-section scroll-mt-6"
            aria-label={`${group.statusLabel} (${group.count})`}
          >
            <div className="jp-section__head">
              <h2 className="jp-section__title">{group.statusLabel}</h2>
              <span className="jp-section__count">{group.count}</span>
            </div>
            <div className="jp-applist">
              {group.applications.length === 0 ? (
                <p className="jp-guest-applist__empty">
                  Inga ansökningar i den här statusen.
                </p>
              ) : (
                group.applications.map((app) => (
                  // F-Pre Punkt 5b: rader är `<Link>` så soft-nav fångas av
                  // `@modal/(.)ansokningar/[id]` → modal (ADR 0053-paritet).
                  <Link
                    key={app.id}
                    href={`/gast/ansokningar/${app.id}`}
                    className="jp-app"
                    aria-label={`${app.role} – ${app.company} – ${group.statusLabel}`}
                  >
                    <div className="jp-job__body">
                      <h3 className="jp-app__title">{app.role}</h3>
                      <div className="jp-app__company">{app.company}</div>
                      <div className="jp-app__meta">
                        <span>{app.source}</span>
                        <span aria-hidden="true"> · </span>
                        <span>{app.updatedAtLabel}</span>
                      </div>
                    </div>
                  </Link>
                ))
              )}
            </div>
          </section>
        ))}
      </div>
    </>
  );
}
