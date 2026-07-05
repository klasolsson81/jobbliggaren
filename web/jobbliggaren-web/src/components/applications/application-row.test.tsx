import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ApplicationRow } from "./application-row";
import type {
  ApplicationDto,
  ApplicationStatus,
  JobAdSummaryDto,
} from "@/lib/types/applications";

// next/link renderas som <a> i jsdom utan extra mock (Next client Link).

// #630 PR 6: radklick sätter drawer-ankaret (klick-Y + trigger-element) för
// höger-drawern (Approach A). Mocka storen så vi kan verifiera anropen.
const setDrawerAnchor = vi.fn();
vi.mock("@/components/applications/drawer-anchor", () => ({
  setDrawerAnchor: (clientY: number, trigger: HTMLElement | null) =>
    setDrawerAnchor(clientY, trigger),
}));

// Fast referenstid → den relativa tids-taggen är deterministisk (injicerad
// `now`, ingen new Date() i raden). Undviker date-flake-klassen
// (reference_oversikt_test_dayofmonth_flake).
const FIXED_NOW = new Date("2026-05-15T12:00:00Z");

const jobAd: JobAdSummaryDto = {
  jobAdId: "ad-1",
  title: "Backend-utvecklare",
  company: "Volvo",
  url: "https://example.com/ad",
  source: "Platsbanken",
  publishedAt: "2026-05-01",
  expiresAt: "2026-06-01",
};

function makeApplication(
  overrides: Partial<ApplicationDto> = {}
): ApplicationDto {
  return {
    id: "11111111-2222-3333-4444-555555555555",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01",
    updatedAt: "2026-05-12",
    // appliedAt 5 kalenderdagar före FIXED_NOW → "Skickad för 5 dagar sedan".
    appliedAt: "2026-05-10",
    jobAd,
    ...overrides,
  };
}

function makeDraft(overrides: Partial<ApplicationDto> = {}): ApplicationDto {
  return makeApplication({
    status: "Draft",
    appliedAt: null,
    // updatedAt 1 kalenderdag före FIXED_NOW → "Uppdaterad i går".
    updatedAt: "2026-05-14",
    ...overrides,
  });
}

describe("ApplicationRow (v3 .jp-app)", () => {
  it("emitterar det DELADE jp-app-radchassit (jp-job≡jp-app, HANDOVER §9)", () => {
    render(<ApplicationRow application={makeApplication()} now={FIXED_NOW} />);
    const link = screen.getByRole("link");
    expect(link).toHaveClass("jp-app");
  });

  it("renderar EXAKT 2 grid-barn (body + actions) utan statusbadge (F5 B1, prototyp-exakt)", () => {
    const { container } = render(
      <ApplicationRow application={makeApplication()} now={FIXED_NOW} />
    );
    const link = screen.getByRole("link");
    // Prototyp pages.jsx ApplicationRow = exakt 2 grid-barn:
    // .jp-job__body + .jp-app__actions. INGEN .jp-app__statusbadge i raden
    // (den 56px-badgen hör till modalen/detaljen).
    expect(link.children).toHaveLength(2);
    expect(link.children[0]).toHaveClass("jp-job__body");
    expect(link.children[1]).toHaveClass("jp-app__actions");
    expect(container.querySelector(".jp-app__statusbadge")).toBeNull();
  });

  it("renders jobtitel + företag separat när jobAd finns", () => {
    render(<ApplicationRow application={makeApplication()} now={FIXED_NOW} />);
    expect(screen.getByText("Backend-utvecklare")).toBeInTheDocument();
    expect(screen.getByText("Volvo")).toBeInTheDocument();
  });

  it("faller tillbaka till mono 'Ansökan #<8>' när jobAd är null", () => {
    render(
      <ApplicationRow
        application={makeApplication({ jobAd: null, jobAdId: null })}
        now={FIXED_NOW}
      />
    );
    const fallback = screen.getByText("Ansökan #11111111");
    expect(fallback).toBeInTheDocument();
    expect(fallback).toHaveClass("jp-mono");
    expect(
      screen.queryByText("Backend-utvecklare")
    ).not.toBeInTheDocument();
  });

  // #336 slice 1 — status är nu en KVADRATISK [data-tag="status-*"]-tagg i
  // höger-kolumnen (ej den rundade .jp-pill). Färgkodad (data-attr) + textetikett
  // (WCAG 1.4.1 — inte färg-enbart).
  it("renderar status som kvadratisk färgkodad .jp-tag med data-tag i actions-kolumnen", () => {
    const { container } = render(
      <ApplicationRow application={makeApplication()} now={FIXED_NOW} />
    );
    // Submitted → Brand → "Skickad".
    const statusTag = screen.getByText("Skickad");
    expect(statusTag).toHaveClass("jp-tag");
    expect(statusTag).toHaveAttribute("data-tag", "status-brand");
    // Bor i höger-kolumnen (jp-app__actions), inte i meta-raden.
    const actions = container.querySelector(".jp-app__actions");
    expect(actions).not.toBeNull();
    expect(actions).toContainElement(statusTag);
    // Den gamla rundade .jp-pill-statusen finns inte längre.
    expect(container.querySelector(".jp-pill")).toBeNull();
  });

  it("mappar status-varianten per STATUS_BADGE_VARIANT (Draft → status-info)", () => {
    render(<ApplicationRow application={makeDraft()} now={FIXED_NOW} />);
    const statusTag = screen.getByText("Utkast");
    expect(statusTag).toHaveAttribute("data-tag", "status-info");
  });

  // #336 slice 1 — relativ tids-tagg i meta-raden (neutral .jp-tag).
  it("renderar 'Skickad för X dagar sedan' (ankrad på appliedAt) för post-submit", () => {
    render(<ApplicationRow application={makeApplication()} now={FIXED_NOW} />);
    // appliedAt 2026-05-10, now 2026-05-15 → 5 dagar.
    const relTag = screen.getByText("Skickad för 5 dagar sedan");
    expect(relTag).toHaveClass("jp-tag");
    // Sitter i meta-raden, inte i actions.
    expect(relTag.closest(".jp-app__meta")).not.toBeNull();
  });

  it("renderar 'Uppdaterad …' (ankrad på updatedAt) för Draft", () => {
    render(<ApplicationRow application={makeDraft()} now={FIXED_NOW} />);
    // updatedAt 2026-05-14, now 2026-05-15 → 1 dag → "i går".
    expect(screen.getByText("Uppdaterad i går")).toBeInTheDocument();
    // Draft visar INTE en "Skickad …"-tagg.
    expect(screen.queryByText(/Skickad/)).not.toBeInTheDocument();
  });

  // #336 slice 1 — den enda diskriminatorn i raden är isDraft = status==="Draft",
  // så ALLA icke-Draft (Acknowledged…OfferReceived OCH terminala Rejected/
  // Accepted/Withdrawn) tar "Skickad"-grenen ankrad på appliedAt. Domänen
  // stämplar AppliedAt en gång på första Submit och skriver aldrig om den
  // (Application.cs:116) → den överlever till terminalt tillstånd. Klas Q1:
  // "Skickad för X sedan" behålls för terminala tillstånd. Pinnar grenen vid
  // ett intermediärt OCH ett terminalt tillstånd så regressionen "bara
  // Submitted bär Skickad" fångas.
  it.each<ApplicationStatus>([
    "Acknowledged",
    "Interviewing",
    "OfferReceived",
    "Rejected",
    "Accepted",
    "Withdrawn",
  ])(
    "renderar 'Skickad …' (ankrad på appliedAt) för post-submit-tillståndet %s",
    (status) => {
      render(
        <ApplicationRow
          application={makeApplication({ status })}
          now={FIXED_NOW}
        />
      );
      // appliedAt 2026-05-10, now 2026-05-15 → 5 dagar — oavsett status.
      expect(screen.getByText("Skickad för 5 dagar sedan")).toBeInTheDocument();
      // Inget "Sök senast" efter inskickad ansökan, även i terminalt tillstånd.
      expect(screen.queryByText(/Sök senast/)).not.toBeInTheDocument();
    }
  );

  it("faller tillbaka till updatedAt när en post-submit-rad saknar appliedAt (deploy-skew)", () => {
    render(
      <ApplicationRow
        application={makeApplication({ appliedAt: null, updatedAt: "2026-05-13" })}
        now={FIXED_NOW}
      />
    );
    // appliedAt null → ankras på updatedAt 2026-05-13 → 2 dagar, men verbet är
    // fortfarande "Skickad" (post-submit-tillstånd).
    expect(screen.getByText("Skickad för 2 dagar sedan")).toBeInTheDocument();
  });

  it("clampar framtida datum till 'i dag' (negativa dagar)", () => {
    render(
      <ApplicationRow
        application={makeApplication({ appliedAt: "2026-05-20" })}
        now={FIXED_NOW}
      />
    );
    expect(screen.getByText("Skickad i dag")).toBeInTheDocument();
  });

  // #336 slice 1 — "Sök senast" visas ENBART för Draft (sista ansökningsdag är
  // bara relevant innan du sökt).
  it("renderar 'Sök senast <date>' för Draft", () => {
    render(<ApplicationRow application={makeDraft()} now={FIXED_NOW} />);
    expect(screen.getByText(/Sök senast/)).toBeInTheDocument();
    expect(screen.getByText("1 juni 2026")).toBeInTheDocument();
  });

  it("utelämnar 'Sök senast' för post-submit-rader (Submitted)", () => {
    render(<ApplicationRow application={makeApplication()} now={FIXED_NOW} />);
    expect(screen.queryByText(/Sök senast/)).not.toBeInTheDocument();
  });

  it("utelämnar 'Sök senast' för Draft när expiresAt är null", () => {
    render(
      <ApplicationRow
        application={makeDraft({ jobAd: { ...jobAd, expiresAt: null } })}
        now={FIXED_NOW}
      />
    );
    expect(screen.queryByText(/Sök senast/)).not.toBeInTheDocument();
  });

  it("renderar kort-id (#8) i meta-raden", () => {
    render(<ApplicationRow application={makeApplication()} now={FIXED_NOW} />);
    expect(screen.getByText("#11111111")).toBeInTheDocument();
  });

  it("länkar hela raden till /ansokningar/<id> (intercept → drawer)", () => {
    render(<ApplicationRow application={makeApplication()} now={FIXED_NOW} />);
    const link = screen.getByRole("link");
    expect(link).toHaveAttribute(
      "href",
      "/ansokningar/11111111-2222-3333-4444-555555555555"
    );
  });

  // #630 PR 6 (Approach A): vanligt klick sätter drawer-ankaret (klick-Y +
  // trigger); modifierat klick (ny flik/fönster) navigerar till fullsidan och
  // sätter EJ ankaret (drawern öppnas inte där).
  it("sätter drawer-ankaret vid vanligt klick men inte vid modifierat klick", () => {
    setDrawerAnchor.mockClear();
    render(<ApplicationRow application={makeApplication()} now={FIXED_NOW} />);
    const link = screen.getByRole("link");

    fireEvent.click(link, { clientY: 420 });
    expect(setDrawerAnchor).toHaveBeenCalledTimes(1);
    expect(setDrawerAnchor).toHaveBeenCalledWith(420, link);

    fireEvent.click(link, { clientY: 420, metaKey: true });
    fireEvent.click(link, { clientY: 420, ctrlKey: true });
    fireEvent.click(link, { clientY: 420, shiftKey: true });
    // Fortfarande bara det första (vanliga) klicket räknat.
    expect(setDrawerAnchor).toHaveBeenCalledTimes(1);
  });

  it("har en tillgänglig aria-label med titel, företag och status", () => {
    render(<ApplicationRow application={makeApplication()} now={FIXED_NOW} />);
    expect(
      screen.getByRole("link", {
        name: "Backend-utvecklare, Volvo, Skickad",
      })
    ).toBeInTheDocument();
  });
});
