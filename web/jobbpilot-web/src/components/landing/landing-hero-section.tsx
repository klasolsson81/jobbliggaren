"use client";

import { useRouter } from "next/navigation";
import { ArrowRight, Search } from "lucide-react";

/**
 * LandingHeroSection — produkt-forward ljus hero (G4-redesign, CTO Riktning A).
 *
 * Tidigare grön bakgrundsbox bakom H1 är borttagen: hero-canvasen är nu LJUS
 * (`--jp-surface`) och den enda gröna ytan är primär-CTA:ns fill plus ett litet
 * scoped hero-band-fragment INUTI produkt-peeken (ADR 0068-undantaget).
 *
 * Klient-island enbart för CTA-navigeringen (`useRouter`). Per Klas-direktiv
 * 2026-05-24 (Steg 5 closed-beta) finns ingen "Skapa konto"-CTA. Två CTA:er:
 *  - PRIMÄR "Anmäl till väntelista" → `/vantelista` (`.jp-btn--primary`,
 *    accent-800 grön fill + vit text — RÄTT på ljus canvas, ej grön-på-grön).
 *  - SEKUNDÄR "Utforska som gäst"/"Till översikt" (`.jp-btn--secondary`,
 *    vit/border + ink-text). F-Pre Punkt 5: target `/gast/oversikt` (anonym)
 *    resp `/oversikt` (inloggad); inloggad-state byter även CTA-texten.
 *
 * HÖGER: produkt-peek — STATISK ren markup (ingen interaktivitet, ingen
 * klient-JS, ingen state, ingen animation). Visar produkten i miniatyr: ett
 * litet grönt hero-band-fragment (scoped `--jp-hero-gradient`) med ett
 * sökfält-ATTRAPP (ren markup, ingen riktig `<input>`) ovanför två
 * resultat-kort i flat/papper-stil. `aria-hidden` eftersom peeken är en
 * dekorativ produkt-illustration, inte interaktivt UI.
 *
 * Civic-utility-disciplin: inga Sparkles, inga gradient-bg utöver det scopade
 * peek-bandet, inga trust-pills. CTA-ikon (ArrowRight) är funktionell.
 */
export function LandingHeroSection({
  isAuthenticated,
}: {
  isAuthenticated: boolean;
}) {
  const router = useRouter();

  return (
    <section className="jp-land-hero">
      <div className="jp-land-hero__inner">
        <div className="jp-land-hero__copy">
          <h1 className="jp-land-hero__title">
            Håll ordning i ditt jobbsökande
          </h1>
          <p className="jp-land-hero__lede">
            Sök bland aktiva annonser från Platsbanken, skapa CV och brev, och följ upp
            varje ansökan, från utkast till svar.
          </p>
          <div className="jp-land-hero__ctas">
            <button
              type="button"
              className="jp-btn jp-btn--lg jp-btn--primary"
              onClick={() => router.push("/vantelista")}
            >
              Anmäl till väntelista
            </button>
            <button
              type="button"
              className="jp-btn jp-btn--lg jp-btn--secondary"
              onClick={() =>
                router.push(isAuthenticated ? "/oversikt" : "/gast/oversikt")
              }
            >
              {isAuthenticated ? "Till översikt" : "Utforska som gäst"}{" "}
              <ArrowRight size={16} aria-hidden="true" />
            </button>
          </div>
        </div>

        {/* Produkt-peek: dekorativ, statisk produkt-illustration. aria-hidden
            eftersom den inte är interaktivt UI och inte ska läsas som sådant
            av skärmläsare (G4 CTO-spec). */}
        <div className="jp-land-peek" aria-hidden="true">
          {/* Scoped hero-band-fragment (ADR 0068-undantaget) — miniatyr av
              /jobb-bannern. Sökfältet är en ATTRAPP (ren markup, ingen input). */}
          <div className="jp-land-peek__band">
            <span className="jp-land-peek__band-kicker">Sök jobb</span>
            <div className="jp-land-peek__search">
              <span className="jp-land-peek__search-text">
                Systemutvecklare · Göteborg
              </span>
              <span className="jp-land-peek__search-btn">
                <Search size={15} aria-hidden="true" />
                Sök
              </span>
            </div>
          </div>

          {/* Resultat-kort i flat/papper-stil (hairlines, mono för ID/datum). */}
          <ul className="jp-land-peek__list">
            <li className="jp-land-peek__card">
              <div className="jp-land-peek__card-head">
                <span className="jp-land-peek__card-id">A-2841</span>
                <span className="jp-land-peek__card-date">2026-06-09</span>
              </div>
              <div className="jp-land-peek__card-title">
                Backend-utvecklare .NET
              </div>
              <div className="jp-land-peek__card-meta">
                Volvo Cars Sverige AB · Göteborg
              </div>
            </li>
            <li className="jp-land-peek__card">
              <div className="jp-land-peek__card-head">
                <span className="jp-land-peek__card-id">A-2838</span>
                <span className="jp-land-peek__card-date">2026-06-08</span>
              </div>
              <div className="jp-land-peek__card-title">
                Systemutvecklare fullstack
              </div>
              <div className="jp-land-peek__card-meta">
                Folksam IT · Stockholm
              </div>
            </li>
          </ul>
        </div>
      </div>
    </section>
  );
}
