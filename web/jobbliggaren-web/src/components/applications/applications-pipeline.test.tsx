import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApplicationsPipeline } from "./applications-pipeline";
import { PIPELINE_ORDER } from "@/lib/applications/status";
import type {
  ApplicationAttentionSignal,
  ApplicationDto,
  ApplicationStatus,
  JobAdSummaryDto,
  PipelineGroupDto,
} from "@/lib/dto/applications";

// next/link renderas som <a> i jsdom utan extra mock.

// Server-beräknad referenstidpunkt passeras som ISO-sträng (ADR 0092 D2 /
// #336-determinism) — ön rekonstruerar Date en gång.
const FIXED_NOW_ISO = "2026-05-20T12:00:00Z";

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
  overrides: Partial<ApplicationDto> = {},
): ApplicationDto {
  return {
    id: "11111111-2222-3333-4444-555555555555",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01",
    updatedAt: "2026-05-10",
    jobAd,
    ...overrides,
  };
}

// Bygger pipelinen. `signals` mappar status -> per-rad attention-signaler
// (positionellt alignat med applications). Saknad post = "None".
function makePipeline(
  populated: Partial<Record<ApplicationStatus, number>>,
  signals: Partial<Record<ApplicationStatus, ApplicationAttentionSignal[]>> = {},
): PipelineGroupDto[] {
  return PIPELINE_ORDER.map((status) => {
    const n = populated[status] ?? 0;
    const sig = signals[status] ?? [];
    return {
      status,
      count: n,
      applications: Array.from({ length: n }, (_, i) =>
        makeApplication({
          id: `${status}-${i}-0000-0000-000000000000`,
          status,
          jobAd: { ...jobAd, title: `${status}-titel-${i}` },
          attentionSignal: sig[i] ?? "None",
        }),
      ),
    };
  });
}

function renderPipeline(groups: PipelineGroupDto[]) {
  return render(
    <ApplicationsPipeline groups={groups} nowIso={FIXED_NOW_ISO} />,
  );
}

function getQueue() {
  return screen.getByRole("region", { name: "Kräver åtgärd" });
}
function getAllApps() {
  return screen.getByRole("region", { name: "Alla ansökningar" });
}

describe("ApplicationsPipeline — Lista-sektioner (2a)", () => {
  it("renderar bara sektioner med count > 0, i pipeline-ordning", () => {
    renderPipeline(makePipeline({ Accepted: 1, Draft: 2 }));

    expect(document.getElementById("status-Draft")).not.toBeNull();
    expect(document.getElementById("status-Accepted")).not.toBeNull();
    const draftIdx = document.body.innerHTML.indexOf("status-Draft");
    const acceptedIdx = document.body.innerHTML.indexOf("status-Accepted");
    expect(draftIdx).toBeLessThan(acceptedIdx);
  });

  it("default-öppna = Skickad/Intervju bokad/Erbjudande; övriga kollapsade", () => {
    renderPipeline(
      makePipeline({
        Draft: 1,
        Submitted: 1,
        InterviewScheduled: 1,
        OfferReceived: 1,
        Rejected: 1,
      }),
    );

    // Öppna (design §5).
    expect(document.getElementById("status-Submitted-list")).not.toBeNull();
    expect(
      document.getElementById("status-InterviewScheduled-list"),
    ).not.toBeNull();
    expect(document.getElementById("status-OfferReceived-list")).not.toBeNull();
    // Kollapsade (Draft är INTE default-öppen i 2a).
    expect(document.getElementById("status-Draft-list")).toBeNull();
    expect(document.getElementById("status-Rejected-list")).toBeNull();
  });

  it("WAI-accordion: h2 wrappar knappen, ingen aria-label, count i namnet", () => {
    renderPipeline(makePipeline({ Submitted: 3 }));

    const heading = screen.getByRole("heading", { name: /Skickad/ });
    const toggle = within(heading).getByRole("button");
    expect(toggle).not.toHaveAttribute("aria-label");
    expect(toggle.textContent ?? "").toContain("Skickad");
    // Antalet (shown) ligger i det synliga namnet.
    expect(toggle.querySelector(".jp-section__count")).toHaveTextContent("3");
  });

  it("klick på head växlar aria-expanded och visar/döljer raderna", async () => {
    const user = userEvent.setup();
    renderPipeline(makePipeline({ Submitted: 1 }));

    // Scopa till sektionen: railen har också en "Skickad"-knapp.
    const toggle = within(
      document.getElementById("status-Submitted")!,
    ).getByRole("button", { name: /Skickad/ });
    expect(toggle).toHaveAttribute("aria-expanded", "true");

    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(document.getElementById("status-Submitted-list")).toBeNull();

    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(document.getElementById("status-Submitted-list")).not.toBeNull();
  });

  it("kollapsad sektion visar 'Klicka för att visa'", () => {
    renderPipeline(makePipeline({ Rejected: 1 }));

    const section = document.getElementById("status-Rejected")!;
    expect(section).toHaveTextContent("Klicka för att visa");
  });

  it("'AVSLUT & VILANDE'-kicker före första terminala gruppen", () => {
    renderPipeline(makePipeline({ Submitted: 1, Accepted: 1 }));

    const kicker = screen.getByText("Avslut och vilande");
    const kickerIdx = document.body.innerHTML.indexOf("Avslut och vilande");
    const acceptedIdx = document.body.innerHTML.indexOf("status-Accepted");
    const submittedIdx = document.body.innerHTML.indexOf("status-Submitted");
    expect(kicker).toBeInTheDocument();
    // Kickern ligger efter Skickad (aktiv) men före Accepterad (terminal).
    expect(submittedIdx).toBeLessThan(kickerIdx);
    expect(kickerIdx).toBeLessThan(acceptedIdx);
  });
});

describe("ApplicationsPipeline — 'Visa fler' (cap 10)", () => {
  it("visar max 10 rader och en 'Visa fler (N)'-knapp; count = shown", () => {
    renderPipeline(makePipeline({ Submitted: 13 }));

    const section = document.getElementById("status-Submitted")!;
    const list = document.getElementById("status-Submitted-list")!;
    expect(within(list).getAllByRole("link")).toHaveLength(10);
    expect(
      within(section).getByRole("button", { name: "Visa fler (3)" }),
    ).toBeInTheDocument();
    expect(section.querySelector(".jp-section__count")).toHaveTextContent("13");
  });

  it("klick på 'Visa fler' expanderar hela sektionen och tar bort knappen", async () => {
    const user = userEvent.setup();
    renderPipeline(makePipeline({ Submitted: 13 }));

    await user.click(screen.getByRole("button", { name: "Visa fler (3)" }));

    const list = document.getElementById("status-Submitted-list")!;
    expect(within(list).getAllByRole("link")).toHaveLength(13);
    expect(
      screen.queryByRole("button", { name: /Visa fler/ }),
    ).not.toBeInTheDocument();
  });
});

describe("ApplicationsPipeline — 2a DUPLICAT-doktrin (kön ⊄ MOVE)", () => {
  it("en ansökan med signal ligger i BÅDE kön OCH sin statusgrupp", () => {
    // Submitted är default-öppen → båda ytorna synliga.
    renderPipeline(
      makePipeline({ Submitted: 1 }, { Submitted: ["OverdueFollowUp"] }),
    );

    const queue = getQueue();
    const section = document.getElementById("status-Submitted")!;

    // Kortet i kön OCH raden i sektionen refererar SAMMA app (två länkar).
    expect(within(queue).getByText("Submitted-titel-0")).toBeInTheDocument();
    expect(within(section).getByText("Submitted-titel-0")).toBeInTheDocument();
    // Listan är komplett: count räknar appen (ej dränerad som gamla MOVE).
    expect(section.querySelector(".jp-section__count")).toHaveTextContent("1");
  });
});

describe("ApplicationsPipeline — sök", () => {
  it("filtrerar Lista-sektionerna på roll/företag och tvingar gruppen öppen", async () => {
    const user = userEvent.setup();
    renderPipeline(makePipeline({ Rejected: 2 }));

    // Rejected är default-kollapsad; sök tvingar den öppen (design §5).
    const search = screen.getByRole("searchbox", {
      name: "Sök bland ansökningar",
    });
    await user.type(search, "Rejected-titel-1");

    const section = document.getElementById("status-Rejected")!;
    expect(
      within(section).getByRole("button", { name: /Nekad/ }),
    ).toHaveAttribute("aria-expanded", "true");
    expect(within(section).getByText("Rejected-titel-1")).toBeInTheDocument();
    expect(
      within(section).queryByText("Rejected-titel-0"),
    ).not.toBeInTheDocument();
  });

  it("sök som inte matchar visar tomläge för Alla ansökningar", async () => {
    const user = userEvent.setup();
    renderPipeline(makePipeline({ Submitted: 1 }));

    await user.type(
      screen.getByRole("searchbox", { name: "Sök bland ansökningar" }),
      "zzz-ingen-traff",
    );
    expect(
      within(getAllApps()).getByText(
        "Inga ansökningar matchar sökningen eller filtret.",
      ),
    ).toBeInTheDocument();
  });
});

describe("ApplicationsPipeline — stegfilter", () => {
  it("klick på en rail-cell filtrerar listan, visar filterchip och kan rensas", async () => {
    const user = userEvent.setup();
    renderPipeline(makePipeline({ Submitted: 1, Rejected: 1 }));

    // Klicka på Skickad-cellen i railen.
    const rail = screen.getByRole("group", { name: "Filtrera på steg" });
    const submittedCell = within(rail).getByRole("button", {
      name: /Skickad/,
    });
    await user.click(submittedCell);

    // Bara Skickad-sektionen kvar; Nekad borta.
    expect(submittedCell).toHaveAttribute("aria-pressed", "true");
    expect(document.getElementById("status-Submitted")).not.toBeNull();
    expect(document.getElementById("status-Rejected")).toBeNull();
    // Filterchip synlig.
    expect(screen.getByText(/Filter:/)).toBeInTheDocument();

    // Klick igen (toggle) rensar filtret.
    await user.click(submittedCell);
    expect(submittedCell).toHaveAttribute("aria-pressed", "false");
    expect(document.getElementById("status-Rejected")).not.toBeNull();
  });
});

describe("ApplicationsPipeline — tomt öråt", () => {
  it("tomma grupper: kön visar tomläge och listan visar tomläge", () => {
    renderPipeline(makePipeline({}));

    // Kön alltid närvarande (2a) — med streckat tomläge.
    expect(
      within(getQueue()).getByText("Inget kräver åtgärd just nu."),
    ).toBeInTheDocument();
    expect(
      within(getAllApps()).getByText(
        "Inga ansökningar matchar sökningen eller filtret.",
      ),
    ).toBeInTheDocument();
  });
});
