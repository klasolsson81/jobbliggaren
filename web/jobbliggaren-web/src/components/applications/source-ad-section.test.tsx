import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { SourceAdSection } from "./source-ad-section";
import type { AdSnapshotDto, JobAdSummaryDto } from "@/lib/types/applications";

/**
 * SourceAdSection — #805-3 (Beslut B). Guarden som avgör vad en ansökan får
 * SÄGA om källans annons bor på ETT ställe (SPOT), renderad av detaljkroppen
 * (`ApplicationDetailBody`) i modalen och på fullsidan.
 *
 * Kroppens svit pinnar att komponenten är INKOPPLAD. Denna svit pinnar dess
 * TILLSTÅNDSRUM uttömmande. Skälet är rotorsaken själv: när guardens grenar
 * bara täcks transitivt räcker det att ingen yta råkar rendera en gren för att
 * den ska kunna vara fel i två releaser utan att en enda test bli röd. Guarden
 * har egna tester nu.
 *
 * Axlarna: `jobAd` (null | rad) × `jobAdId` (länkad | manuell) × `status`
 * (Active | icke-Active | saknad) × `url` (null | satt) × `preservedAd`.
 */

function makeJobAd(overrides: Partial<JobAdSummaryDto> = {}): JobAdSummaryDto {
  return {
    jobAdId: "ad-1",
    title: "Backend-utvecklare",
    company: "Volvo",
    url: "https://example.com/ad",
    source: "Platsbanken",
    publishedAt: "2026-05-01",
    expiresAt: "2026-06-01",
    status: "Active",
    ...overrides,
  };
}

function makeManualJobAd(
  overrides: Partial<JobAdSummaryDto> = {}
): JobAdSummaryDto {
  return makeJobAd({
    // Strukturell sanning: ingen JobAd-rad. BE sätter Source="Manual" och
    // Status=null (ingen annonsrad ⇒ ingen arkivering ⇒ ingen livs-utsaga).
    jobAdId: null,
    title: "Manuell titel",
    company: "Manuellt företag",
    url: "https://example.com/manuell",
    source: "Manual",
    publishedAt: null,
    status: null,
    ...overrides,
  });
}

const snapshot: AdSnapshotDto = {
  title: "Systemutvecklare .NET",
  company: "Spotify",
  location: "Stockholm",
  url: "https://example.com/saved-ad",
  source: "Platsbanken",
  publishedAt: "2026-04-10T08:00:00Z",
  expiresAt: "2026-05-10T08:00:00Z",
  description: "Vi söker en utvecklare med erfarenhet av distribuerade system.",
  contacts: [],
  capturedAt: "2026-04-12T08:00:00Z",
};

describe("SourceAdSection (#805-3, Beslut B)", () => {
  // ── Ingen annonsrad ────────────────────────────────────────────────────
  it("renderar ingenting när ansökan saknar annonsrad (enbart personligt brev)", () => {
    const { container } = render(
      <SourceAdSection jobAd={null} preservedAd={null} />
    );
    expect(container.firstChild).toBeNull();
  });

  // ── Live ───────────────────────────────────────────────────────────────
  it("LIVE (Active) → säker utlänk till källans annons", () => {
    render(<SourceAdSection jobAd={makeJobAd()} preservedAd={null} />);

    expect(screen.getByText("Om annonsen")).toBeInTheDocument();
    const link = screen.getByRole("link", {
      name: "Visa annonsen hos Platsbanken (öppnas i ny flik)",
    });
    expect(link).toHaveAttribute("href", "https://example.com/ad");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("LIVE + bevarad kopia finns → länken vinner, kopian visas EJ (den är borta-lägets fallback)", () => {
    render(<SourceAdSection jobAd={makeJobAd()} preservedAd={snapshot} />);

    expect(
      screen.getByRole("link", { name: /Visa annonsen/ })
    ).toBeInTheDocument();
    // ADR 0086: snapshotten är en fallback för när annonsen är borta — inte en
    // dubblett av den levande annonsen.
    expect(
      screen.queryByText("Om annonsen (sparad kopia)")
    ).not.toBeInTheDocument();
  });

  // ── Borta ──────────────────────────────────────────────────────────────
  it("ARKIVERAD + bevarad kopia → panelen, INGEN länk (en död länk är sämre än ingen)", () => {
    render(
      <SourceAdSection
        jobAd={makeJobAd({ status: "Archived" })}
        preservedAd={snapshot}
      />
    );

    expect(
      screen.getByText("Om annonsen (sparad kopia)")
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "Vi söker en utvecklare med erfarenhet av distribuerade system."
      )
    ).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
  });

  // #1827 Minor 10: the saved copy's notice is a plain line, and its rows pair a label with a
  // value (the three-column row is the match section's).
  it("ARKIVERAD + bevarad kopia → noten är en rad utan ruta, raderna parar etikett och värde", () => {
    const { container } = render(
      <SourceAdSection
        jobAd={makeJobAd({ status: "Archived" })}
        preservedAd={snapshot}
      />
    );

    expect(
      screen.getByText(/Kopian är från/).closest(".jp-modal__match")
    ).toBeNull();
    const rows = container.querySelectorAll(".jp-modal__matchrow");
    expect(rows.length).toBeGreaterThan(0);
    for (const row of rows) {
      expect(row).toHaveClass("jp-modal__matchrow--pair");
    }
  });

  // A closing move (Accepted, Rejected, Withdrawn) runs `AdSnapshot.WithoutAdBody()` in
  // `Application.TransitionTo`, which drops the copy's text and its contacts together.
  it("ARKIVERAD + minimerad kopia → metadata och minimeringsnoten, ingen annonstext", () => {
    render(
      <SourceAdSection
        jobAd={makeJobAd({ status: "Archived" })}
        preservedAd={{ ...snapshot, description: null, contacts: [] }}
      />
    );

    expect(
      screen.getByText("Om annonsen (sparad kopia)")
    ).toBeInTheDocument();
    expect(screen.getByText("Stockholm")).toBeInTheDocument();
    expect(
      screen.getByText("Annonstexten raderades när ansökan avslutades.")
    ).toBeInTheDocument();
    expect(
      screen.queryByText(
        "Vi söker en utvecklare med erfarenhet av distribuerade system."
      )
    ).not.toBeInTheDocument();
  });

  it("RADERAD (Erased) → behandlas som borta (default-deny, inte den naiva inversen !== Archived)", () => {
    // Domänen har TRE statusvärden (Active | Archived | Erased — det writerlösa
    // Expired retirerades i #886), och Art. 17-tombstonens status når den här
    // ytan på riktigt (lös z.string()-typning, ingen Erased-mask på
    // ansöknings-läsvägen). Hade guarden kodats som "allt utom Archived är
    // live" hade den skeppat en länk till en RADERAD annons exakt här.
    render(
      <SourceAdSection
        jobAd={makeJobAd({ status: "Erased" })}
        preservedAd={snapshot}
      />
    );

    expect(
      screen.getByText("Om annonsen (sparad kopia)")
    ).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
  });

  it("OKÄNT framtida statusvärde → inte live (liveness hävdas ENDAST på positivt Active)", () => {
    // DTO-schemat typar status löst (z.string()) just för att ett nytt
    // domänvärde ska degradera till "inte live" i stället för att hard-faila
    // parse av hela ansökningslistan. Detta test pinnar den degraderingen.
    render(
      <SourceAdSection
        jobAd={makeJobAd({ status: "Paused" })}
        preservedAd={snapshot}
      />
    );

    expect(
      screen.getByText("Om annonsen (sparad kopia)")
    ).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
  });

  it("BORTA utan bevarad kopia (ansökan skapad före #315) → lugn not, ingen länk", () => {
    render(
      <SourceAdSection
        jobAd={makeJobAd({ status: "Archived" })}
        preservedAd={null}
      />
    );

    expect(
      screen.getByText("Annonsen är inte längre aktiv hos Platsbanken.")
    ).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
  });

  // ── Deploy-skew: status saknas ─────────────────────────────────────────
  //
  // Den svåraste grenen, och den enda där en tyst mutation vore osynlig i
  // ytornas sviter: `isGone` kräver `status != null`. Släpper man det villkoret
  // (`status !== "Active"` räcker ju "uppenbarligen") blir en skewad respons
  // felläst som BORTA — och vi påstår "Annonsen är inte längre aktiv" om en
  // annons som mycket väl kan ligga uppe. Det är samma sorts osanning som den
  // döda länken, bara åt andra hållet. Utan asserten "ingen borta-not" nedan
  // passerar den mutationen.
  it("DEPLOY-SKEW (status saknas) → hävdar VARKEN live eller borta: ingen länk, ingen borta-not, ingen kopia", () => {
    const { status: _omitted, ...withoutStatus } = makeJobAd();
    const { container } = render(
      <SourceAdSection jobAd={withoutStatus} preservedAd={snapshot} />
    );

    // Ingen länk — vi vet inte om URL:en fortfarande svarar.
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
    // …och ingen BORTA-utsaga heller. Vi vet inte att den är borta.
    expect(screen.queryByText(/inte längre aktiv/)).toBeNull();
    expect(
      screen.queryByText("Om annonsen (sparad kopia)")
    ).not.toBeInTheDocument();
    // Ingen halv-renderad sektion: vi säger ingenting alls.
    expect(container.firstChild).toBeNull();
  });

  // ── Manuell ────────────────────────────────────────────────────────────
  it("MANUELL med sparad url → länken användaren själv sparade, utan källa-påstående", () => {
    render(<SourceAdSection jobAd={makeManualJobAd()} preservedAd={null} />);

    // aria-label:n utelämnar källan — annars: "Visa annonsen hos Manuellt".
    const link = screen.getByRole("link", {
      name: "Visa annonsen (öppnas i ny flik)",
    });
    expect(link).toHaveAttribute("href", "https://example.com/manuell");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    // Ingen JobAd-rad ⇒ ingen arkivering ⇒ ingen borta-utsaga.
    expect(screen.queryByText(/inte längre aktiv/)).toBeNull();
  });

  it("MANUELL utan url → renderar ingenting (ingen tom sektionsrubrik)", () => {
    const { container } = render(
      <SourceAdSection
        jobAd={makeManualJobAd({ url: null })}
        preservedAd={null}
      />
    );
    expect(container.firstChild).toBeNull();
  });
});

describe("the saved panel delegates contacts to the shared detail body (#1944)", () => {
  it.each([[[]], [[{ name: "Kontakt Test", role: null, email: "contact@example.test", phone: null, isDerived: false }]]])(
    "renders neither a duplicate contact region nor notice", (contacts) => {
      render(<SourceAdSection jobAd={makeJobAd({ status: "Archived" })} preservedAd={{ ...snapshot, contacts }} />);
      expect(screen.getByText("Om annonsen (sparad kopia)")).toBeInTheDocument();
      expect(screen.queryByRole("region", { name: "Kontakt" })).toBeNull();
      expect(screen.queryByRole("link", { name: "Är du kontaktperson i annonsen? Läs hur vi behandlar kontaktuppgifter." })).toBeNull();
    }
  );
});
