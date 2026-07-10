import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { ApplicationActionsProvider } from "./application-actions";
import { ApplicationRow } from "./application-row";
import {
  dismissApplicationToast,
  getApplicationToastSnapshot,
} from "@/lib/applications/toast-store";
import type {
  ApplicationDto,
  JobAdSummaryDto,
} from "@/lib/types/applications";

// next/link renderas som <a> i jsdom utan extra mock (Next client Link).

// #630 PR 7: raden muterar via providerns server actions — mocka modulen
// (samma idiom som status-edit-card.test) så klick kan verifieras utan nät.
const transitionStatusAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
const logFollowUpAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction,
  logFollowUpAction,
}));

// Fast referenstid → tids-deriveringarna ("N dagar i steget", väntetids-tagg)
// är deterministiska (injicerad `now`, ingen new Date() i raden). Undviker
// date-flake-klassen (reference_oversikt_test_dayofmonth_flake).
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
    appliedAt: "2026-05-10",
    // 5 kalenderdagar före FIXED_NOW → "5 dagar i steget".
    lastStatusChangeAt: "2026-05-10",
    jobAd,
    ...overrides,
  };
}

function renderRow(
  application: ApplicationDto,
  props: Partial<React.ComponentProps<typeof ApplicationRow>> = {},
) {
  return render(
    <ApplicationActionsProvider>
      <ApplicationRow application={application} now={FIXED_NOW} {...props} />
    </ApplicationActionsProvider>,
  );
}

beforeEach(() => {
  transitionStatusAction.mockClear();
  logFollowUpAction.mockClear();
  dismissApplicationToast();
});

describe("ApplicationRow (2a, #630 PR 7)", () => {
  it("emitterar det DELADE jp-app-chassit + den ADDITIVA jp-app--actions-modifiern", () => {
    const { container } = renderRow(makeApplication());
    const row = container.querySelector("article");
    expect(row).toHaveClass("jp-app");
    expect(row).toHaveClass("jp-app--actions");
  });

  it("renderar EXAKT 3 grid-zoner (body + signals + actions)", () => {
    const { container } = renderRow(makeApplication());
    const row = container.querySelector("article")!;
    expect(row.children).toHaveLength(3);
    expect(row.children[0]).toHaveClass("jp-job__body");
    expect(row.children[1]).toHaveClass("jp-app__signals");
    expect(row.children[2]).toHaveClass("jp-app__actions");
  });

  it("titeln är radens ENDA länk (länk-overlay, ingen nästlad interaktivitet i ankaret)", () => {
    renderRow(makeApplication());
    const links = screen.getAllByRole("link");
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveClass("jp-app__rowlink");
    expect(links[0]).toHaveAttribute(
      "href",
      "/ansokningar/11111111-2222-3333-4444-555555555555"
    );
    // Knapparna ligger UTANFÖR ankaret (giltig HTML / a11y).
    expect(links[0]!.querySelector("button")).toBeNull();
  });

  it("renders jobtitel + företag separat när jobAd finns", () => {
    renderRow(makeApplication());
    expect(screen.getByText("Backend-utvecklare")).toBeInTheDocument();
    expect(screen.getByText("Volvo")).toBeInTheDocument();
  });

  it("faller tillbaka till mono 'Ansökan #<8>' när jobAd är null", () => {
    renderRow(makeApplication({ jobAd: null, jobAdId: null }));
    const fallback = screen.getByText("Ansökan #11111111");
    expect(fallback).toBeInTheDocument();
    expect(fallback.closest("h3")).toHaveClass("jp-mono");
  });

  it("renderar status som kvadratisk färgkodad .jp-tag i signal-zonen", () => {
    const { container } = renderRow(makeApplication());
    const statusTag = screen.getByText("Skickad");
    expect(statusTag).toHaveClass("jp-tag");
    // Submitted → STATUS_BADGE_VARIANT Info → "status-info" (#683, design §11).
    expect(statusTag).toHaveAttribute("data-tag", "status-info");
    expect(container.querySelector(".jp-app__signals")).toContainElement(
      statusTag
    );
  });

  it("renderar 'N dagar i steget' ur lastStatusChangeAt (aldrig fabricerat)", () => {
    renderRow(makeApplication());
    expect(screen.getByText("5 dagar i steget")).toBeInTheDocument();
  });

  it("utelämnar dagar-i-steget när lastStatusChangeAt saknas (deploy-skew)", () => {
    renderRow(makeApplication({ lastStatusChangeAt: undefined }));
    expect(screen.queryByText(/i steget/)).not.toBeInTheDocument();
  });

  // §11 bråttom-taggar — data-grundade, keyade på backend-signalen (SSOT).
  it("renderar väntetids-taggen för NoResponseNudge (effektiv väntetid, D5)", () => {
    renderRow(
      makeApplication({
        attentionSignal: "NoResponseNudge",
        lastStatusChangeAt: "2026-04-25", // 20 dagar
        lastFollowUpAt: null,
      })
    );
    const tag = screen.getByText("20 dgr utan svar");
    expect(tag).toHaveAttribute("data-urgency", "info");
  });

  it("väntetids-taggen nollställs av en senare uppföljning (min-klockan)", () => {
    renderRow(
      makeApplication({
        attentionSignal: "GhostSuggested",
        lastStatusChangeAt: "2026-04-01",
        lastFollowUpAt: "2026-05-01", // 14 dagar < 44 dagar
      })
    );
    expect(screen.getByText("14 dgr utan svar")).toBeInTheDocument();
  });

  it("renderar DEADLINE-taggen UTAN år (facit §11 — signalen fyrar ≤7 dgr kvar)", () => {
    renderRow(
      makeApplication({
        status: "Draft",
        attentionSignal: "DraftDeadlineApproaching",
      })
    );
    const tag = screen.getByText("Deadline 1 juni");
    expect(tag).toHaveAttribute("data-urgency", "warning");
  });

  it("renderar INGEN bråttom-tagg utan fyrande signal", () => {
    const { container } = renderRow(makeApplication());
    expect(container.querySelector("[data-urgency]")).toBeNull();
  });

  // §5 handlingszonen — default-primär per status (prototyp-facit).
  it("renderar 'Flytta till {nästa}' + 'Byt status' för aktiva steg", () => {
    renderRow(makeApplication());
    expect(
      screen.getByRole("button", { name: "Flytta till Bekräftad" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Byt status" })
    ).toBeInTheDocument();
  });

  it("'Flytta till {nästa}' gör en DIREKT transition (utan dialog) med ångra-toast", async () => {
    renderRow(makeApplication());
    fireEvent.click(
      screen.getByRole("button", { name: "Flytta till Bekräftad" })
    );
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "11111111-2222-3333-4444-555555555555",
        "Acknowledged"
      )
    );
    await waitFor(() => {
      const toast = getApplicationToastSnapshot();
      expect(toast).toMatchObject({
        kind: "statusChange",
        company: "Volvo",
        from: "Submitted",
        to: "Acknowledged",
      });
    });
  });

  it("Utkast-radens primär är 'Slutför och skicka' och öppnar DIALOGEN (mellansteg, §9)", async () => {
    renderRow(makeApplication({ status: "Draft" }));
    fireEvent.click(
      screen.getByRole("button", { name: "Slutför och skicka" })
    );
    // Dialogen öppnas — ingen transition förrän "Skicka ansökan".
    expect(
      await screen.findByRole("button", { name: "Skicka ansökan" })
    ).toBeInTheDocument();
    expect(transitionStatusAction).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Skicka ansökan" }));
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "11111111-2222-3333-4444-555555555555",
        "Submitted"
      )
    );
  });

  it("Ghosted-radens primär är 'Återaktivera' (→ Skickad)", async () => {
    renderRow(makeApplication({ status: "Ghosted" }));
    fireEvent.click(screen.getByRole("button", { name: "Återaktivera" }));
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "11111111-2222-3333-4444-555555555555",
        "Submitted"
      )
    );
  });

  it("terminala rader har ingen primär-knapp men behåller statusmenyn", () => {
    renderRow(makeApplication({ status: "Rejected" }));
    expect(screen.queryByText(/Flytta till/)).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Byt status" })
    ).toBeInTheDocument();
  });

  // Kökortets override (§11): urgens-CTA:n ersätter default-primären och
  // statusmenyn utelämnas (prototyp-facit).
  it("respekterar primaryAction/secondaryAction-overrides + showStatusMenu={false}", () => {
    const primary = vi.fn();
    const secondary = vi.fn();
    renderRow(makeApplication(), {
      primaryAction: { label: "Följ upp", onClick: primary },
      secondaryAction: { label: "Markera som Inget svar", onClick: secondary },
      showStatusMenu: false,
    });
    expect(screen.queryByText(/Flytta till/)).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Byt status" })
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Följ upp" }));
    expect(primary).toHaveBeenCalledTimes(1);
    fireEvent.click(
      screen.getByRole("button", { name: "Markera som Inget svar" })
    );
    expect(secondary).toHaveBeenCalledTimes(1);
  });

  // Radlänken är en ren soft-nav-länk (route-modalen, ADR 0053) — inget
  // klick-ankare (drawer-ankaret pensionerades med drawern, 2026-07-10).
  it("radlänken pekar på detaljroutens href utan klick-sidoeffekter", () => {
    renderRow(makeApplication());
    const link = screen.getByRole("link");
    expect(link).toHaveAttribute(
      "href",
      "/ansokningar/11111111-2222-3333-4444-555555555555",
    );
  });

  // Design-reviewer Minor 5: länknamnet = rolltiteln (rubriken förblir ren i
  // rubrikrotorn); företag + status är BESKRIVNING via aria-describedby.
  it("länknamnet är rolltiteln; företag + status är beskrivning (aria-describedby)", () => {
    renderRow(makeApplication());
    expect(
      screen.getByRole("link", {
        name: "Backend-utvecklare",
        description: "Volvo, Skickad",
      })
    ).toBeInTheDocument();
    // Rubriken förorenas inte av företag/status.
    expect(
      screen.getByRole("heading", { name: "Backend-utvecklare" })
    ).toBeInTheDocument();
  });

  // Providerns felgren (code-reviewer Minor 4): misslyckad action → fel-toast
  // (assertiv), ALDRIG en statusChange-toast.
  it("misslyckad transition publicerar fel-toast, ingen ångra-toast", async () => {
    transitionStatusAction.mockResolvedValueOnce({
      success: false as const,
      error: "Statusbytet misslyckades.",
    } as never);
    renderRow(makeApplication());
    fireEvent.click(
      screen.getByRole("button", { name: "Flytta till Bekräftad" })
    );
    await waitFor(() =>
      expect(getApplicationToastSnapshot()).toMatchObject({
        kind: "error",
        message: "Statusbytet misslyckades.",
      })
    );
  });
});
