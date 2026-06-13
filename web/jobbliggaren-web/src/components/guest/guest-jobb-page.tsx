import { Search } from "lucide-react";
import { GUEST_MOCK } from "@/lib/guest/mock-data";
import { GuestJobAdCard } from "./guest-job-ad-card";

// F-Pre Punkt 5b 2026-05-24 — gäst-mock-jobblistning (CTO Beslut 2). Klonar
// `(app)/jobb`-strukturen visuellt: G1 F4-gradient-platta + sökrad (statisk
// attrapp, ej funktionell — gäst-tree filtrerar inte mock) + lista av
// mock-annonser. Ingen placeholder i sökfältet (Klas hård regel 2026-06-10
// — hint-raden under fältet bär låst-i-demoläget-informationen).
// Klick på rad → `/gast/jobb/[id]` som soft-fångas av
// `(guest)/gast/@modal/(.)jobb/[id]` (commit 2, ADR 0053-paritet).

export function GuestJobbPage() {
  const { jobAds } = GUEST_MOCK;

  return (
    <>
      <section className="jp-hero">
        <div className="jp-hero__inner">
          <div className="jp-hero__plate">
            <div>
              <h1 className="jp-hero__title">Sök jobb</h1>
              <p className="jp-hero__lede">
                Detta är exempelannonser. När du har konto kan du söka och
                spara riktiga annonser från Platsbanken.
              </p>
            </div>

            <div className="jp-hero__panel">
              <div className="jp-hero__searchblock">
                <label
                  htmlFor="guest-jobb-q"
                  className="jp-hero__searchlabels"
                >
                  Sök på ett eller flera ord
                </label>
                <div className="jp-hero__searchrow">
                  <input
                    id="guest-jobb-q"
                    type="search"
                    disabled
                    className="jp-hero__input"
                    aria-describedby="guest-jobb-q-hint"
                  />
                  <button
                    type="button"
                    disabled
                    className="jp-hero__searchbtn"
                    aria-disabled="true"
                  >
                    <Search size={18} aria-hidden="true" /> Sök
                  </button>
                </div>
                <p
                  id="guest-jobb-q-hint"
                  className="text-body-sm"
                  style={{ marginTop: 8, color: "var(--jp-hero-ink-soft)" }}
                >
                  Sökfunktionen är låst i demoläget. Logga in eller anmäl dig
                  till väntelistan för att söka i hela korpus.
                </p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <section
          className="jp-section"
          aria-labelledby="guest-jobb-list"
        >
          <div className="jp-section__head">
            <h2 className="jp-section__title" id="guest-jobb-list">
              Exempelannonser
            </h2>
            <span className="jp-section__count">{jobAds.length}</span>
          </div>
          <div className="jp-jobs">
            {jobAds.map((jobAd) => (
              <GuestJobAdCard key={jobAd.id} jobAd={jobAd} />
            ))}
          </div>
        </section>
      </div>
    </>
  );
}
