"use client";

import { useState } from "react";

/**
 * Språk-toggle (SV/EN) för landing-footern + framtida `/installningar`.
 *
 * HANDOVER §0 punkt 7 + §6.4: toggle placeras EJ i header — endast i
 * landing-footer + Inställningar.
 *
 * EN är **disabled** (no-op + title-attribut) tills översättningar finns.
 * Inget URL- eller cookie-state — denna toggle är visuell-stub idag och
 * kommer wires:as till `next-intl` i framtida fas (matchar BUILD.md §4.3:
 * "Hårdkodade strängar i komponenter — använd `next-intl` med messages/sv.json").
 *
 * Pattern matchar src-v3/shell.jsx LangToggle (verbatim aria-pressed-mönster).
 */
export function LandingLangToggle() {
  // Lokal state — ingen persistens, EN aldrig aktiverbar (no-op-onClick).
  const [lang] = useState<"sv" | "en">("sv");
  return (
    <div className="jp-lang" role="group" aria-label="Språk">
      <button
        type="button"
        className="jp-lang__btn"
        aria-pressed={lang === "sv"}
      >
        SV
      </button>
      <button
        type="button"
        className="jp-lang__btn"
        aria-pressed={false}
        aria-disabled="true"
        title="Engelska är ännu inte implementerat"
        // TODO: aktivera när next-intl messages/en.json är klar
      >
        EN
      </button>
    </div>
  );
}
