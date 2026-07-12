import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { ApplicationDrawerBody } from "./application-drawer-body";
import type {
  AdSnapshotDto,
  ApplicationDetailDto,
} from "@/lib/types/applications";

// Client islands inside the body (NotesSection add form, DrawerStatusActions,
// DrawerLogFollowUpButton) consume the actions.
vi.mock("@/lib/actions/applications", () => ({
  addNoteAction: vi.fn().mockResolvedValue({ success: true }),
  addFollowUpAction: vi.fn().mockResolvedValue({ success: true }),
  recordFollowUpOutcomeAction: vi.fn().mockResolvedValue({ success: true }),
  transitionStatusAction: vi.fn().mockResolvedValue({ success: true }),
  logFollowUpAction: vi.fn().mockResolvedValue({ success: true }),
}));

const NOW = new Date("2026-05-05T12:00:00Z");

function makeDetail(
  overrides: Partial<ApplicationDetailDto> = {},
): ApplicationDetailDto {
  return {
    id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01T08:00:00Z",
    updatedAt: "2026-05-04T08:00:00Z",
    jobAd: {
      jobAdId: "ad-1",
      title: "Backend-utvecklare",
      company: "Volvo",
      url: null,
      source: "Platsbanken",
      publishedAt: null,
      expiresAt: null,
    },
    coverLetter: null,
    followUps: [],
    notes: [],
    ...overrides,
  };
}

const snapshot: AdSnapshotDto = {
  title: "Sparad titel",
  company: "Spotify",
  location: "Stockholm",
  url: null,
  source: "Platsbanken",
  publishedAt: "2026-04-10T08:00:00Z",
  expiresAt: null,
  description: "Sparad annonstext.",
  capturedAt: "2026-04-12T08:00:00Z",
};

describe("ApplicationDrawerBody (§8, interaktiv sedan PR 7)", () => {
  it("renders the status block with STATUS label + value + the describedby id", () => {
    const { container } = render(
      <ApplicationDrawerBody application={makeDetail()} now={NOW} />,
    );
    expect(screen.getByText("Status")).toBeInTheDocument();
    // Scopat: "Skickad" förekommer nu även i stegväljaren (PR 7).
    expect(container.querySelector(".jp-status-block__value")).toHaveTextContent(
      "Skickad",
    );
    // aria-describedby target must exist so the shell's reference never dangles.
    expect(container.querySelector("#jp-modal-desc")).not.toBeNull();
  });

  // ── §8.3–8.5 statusmaskineriet (PR 7) ──────────────────────────────────
  it("renders the primary CTA, the 7-step picker and the park buttons in §8 order", () => {
    const { container } = render(
      <ApplicationDrawerBody application={makeDetail()} now={NOW} />,
    );
    // §8.3: primär-CTA mot nästa steg (Submitted → Bekräftad) + ångra-löftet.
    expect(
      screen.getByRole("button", { name: "Flytta till Bekräftad" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Alla byten kan ångras.")).toBeInTheDocument();
    // §8.4: exakt 7 steg, nuvarande (Skickad) disabled med aria-current.
    const steps = container.querySelectorAll(".jp-steppicker__step");
    expect(steps).toHaveLength(7);
    const current = container.querySelector('[data-state="current"]');
    expect(current).toHaveTextContent("Skickad");
    expect(current).toBeDisabled();
    // §8.5: Nekad (dangertext) / Återtagen / Ghosted ("Inget svar").
    expect(screen.getByRole("button", { name: "Nekad" })).toHaveClass(
      "jp-parkbtn--danger",
    );
    expect(screen.getByRole("button", { name: "Återtagen" })).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Inget svar" }),
    ).toBeInTheDocument();
  });

  it("derives 'N dagar i detta steg' from a REAL recorded status change", () => {
    render(
      <ApplicationDrawerBody
        application={makeDetail({
          statusChanges: [
            { from: "Draft", to: "Submitted", changedAt: "2026-05-02T08:00:00Z" },
          ],
        })}
        now={NOW}
      />,
    );
    // now 05-05 − changedAt 05-02 = 3 days.
    expect(screen.getByText(/3 dagar i detta steg/)).toBeInTheDocument();
    // The timeline carries the real transition (not an updatedAt synthesis).
    expect(screen.getByText("Status: Utkast → Skickad")).toBeInTheDocument();
  });

  it("shows the latest PAST event as 'Senaste', never a future-scheduled follow-up", () => {
    render(
      <ApplicationDrawerBody
        application={makeDetail({
          statusChanges: [
            { from: "Draft", to: "Submitted", changedAt: "2026-05-02T08:00:00Z" },
          ],
          followUps: [
            {
              id: "f1",
              channel: "Phone",
              // Scheduled AFTER now (05-05) → sorts to timeline top but is NOT "senaste".
              scheduledAt: "2026-05-20T08:00:00Z",
              note: null,
              outcome: "Pending",
              outcomeAt: null,
              createdAt: "2026-05-04T08:00:00Z",
            },
          ],
        })}
        now={NOW}
      />,
    );
    // Senaste = the newest PAST event = the Draft→Submitted transition (colon-free
    // in the "Senaste:" context).
    expect(screen.getByText(/Senaste: Utkast → Skickad/)).toBeInTheDocument();
    // The future-scheduled follow-up is NOT surfaced as "Senaste".
    expect(
      screen.queryByText(/Senaste: Uppföljning/),
    ).not.toBeInTheDocument();
  });

  it("OMITS the day-count when no status change is recorded (never fabricate, §5)", () => {
    render(<ApplicationDrawerBody application={makeDetail()} now={NOW} />);
    expect(screen.queryByText(/i detta steg/)).not.toBeInTheDocument();
    // The created event is still present in the timeline.
    expect(screen.getByText("Ansökan skapades")).toBeInTheDocument();
  });

  it("renders follow-ups as a static read-only list (no add affordance)", () => {
    render(
      <ApplicationDrawerBody
        application={makeDetail({
          followUps: [
            {
              id: "f1",
              channel: "Email",
              scheduledAt: "2026-05-03T08:00:00Z",
              note: "Pingade rekryteraren",
              outcome: "Pending",
              outcomeAt: null,
              createdAt: "2026-05-03T08:00:00Z",
            },
          ],
        })}
        now={NOW}
      />,
    );
    expect(screen.getByText("Pingade rekryteraren")).toBeInTheDocument();
    // Raderna är statiska (ingen expand-knapp), och det SCHEMALAGDA formulärets
    // "+ Lägg till uppföljning" finns inte i drawern (Klas-låst §8.6) — men
    // "+ Lägg till" (Logga uppföljning-dialogen) finns i sektionsrubriken.
    expect(
      screen.queryByText("+ Lägg till uppföljning"),
    ).not.toBeInTheDocument();
    // #805 punkt 5: sektionsetiketten bär nu en InfoDialog-"?" (aria-expanded)
    // utanför listan, och drawern har flera role="list". Hitta uppföljningslistan
    // via dess innehåll och verifiera att RADERNA i den är statiska (inga
    // expand-knappar) i read-only-läget — "?" ligger i etiketten, utanför listan.
    const followUpList = screen
      .getAllByRole("list")
      .find((list) => within(list).queryByText("Pingade rekryteraren") != null);
    expect(followUpList).toBeDefined();
    expect(
      within(followUpList!).queryByRole("button", { expanded: false }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "+ Lägg till" }),
    ).toBeInTheDocument();
  });

  it("uses the §8.6 empty copy in the drawer (button explains the wait reset)", () => {
    render(<ApplicationDrawerBody application={makeDetail()} now={NOW} />);
    expect(
      screen.getByText(/Inga uppföljningar ännu/),
    ).toBeInTheDocument();
  });

  it("keeps notes interactive (add-note affordance present)", () => {
    render(<ApplicationDrawerBody application={makeDetail()} now={NOW} />);
    expect(screen.getByText("+ Lägg till anteckning")).toBeInTheDocument();
  });

  it("shows the preserved-ad panel ONLY as a fallback when the live ad is gone", () => {
    const { rerender } = render(
      <ApplicationDrawerBody
        application={makeDetail({ preservedAd: snapshot })}
        now={NOW}
      />,
    );
    // Live ad present → no preserved panel.
    expect(
      screen.queryByText("Om annonsen (sparad kopia)"),
    ).not.toBeInTheDocument();

    // Live ad archived → preserved panel is the fallback.
    rerender(
      <ApplicationDrawerBody
        application={makeDetail({ jobAd: null, preservedAd: snapshot })}
        now={NOW}
      />,
    );
    expect(
      screen.getByText("Om annonsen (sparad kopia)"),
    ).toBeInTheDocument();
    expect(screen.getByText("Stockholm")).toBeInTheDocument();
  });
});
