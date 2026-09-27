import { describe, it, expect, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { transitionStatusAction } from "@/lib/actions/applications";
import { ApplicationDetailBody } from "./application-detail-body";
import type {
  AdSnapshotDto,
  ApplicationDetailDto,
} from "@/lib/types/applications";

// Client islands inside the body (NotesSection add form, ApplicationStatusActions,
// LogFollowUpButton) consume the actions.
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
      url: "https://example.com/ad",
      source: "Platsbanken",
      publishedAt: null,
      expiresAt: null,
      // #805-3: en JobAd-länkad ansökan bär alltid en status; "Active" är
      // prod-normalfallet (annonsen ligger uppe hos källan).
      status: "Active",
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
  contacts: [],
  capturedAt: "2026-04-12T08:00:00Z",
};

describe("ApplicationDetailBody (§8, interaktiv sedan PR 7)", () => {
  it("renders the status block with STATUS label + value + the describedby id", () => {
    const { container } = render(
      <ApplicationDetailBody application={makeDetail()} now={NOW} />,
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
      <ApplicationDetailBody application={makeDetail()} now={NOW} />,
    );
    // §8.3: primär-CTA mot nästa steg (Submitted → Bekräftad).
    expect(
      screen.getByRole("button", { name: "Flytta till Bekräftad" }),
    ).toBeInTheDocument();
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

  it("derives 'N dagar i steget' from a REAL recorded status change", () => {
    render(
      <ApplicationDetailBody
        application={makeDetail({
          statusChanges: [
            { from: "Draft", to: "Submitted", changedAt: "2026-05-02T08:00:00Z" },
          ],
        })}
        now={NOW}
      />,
    );
    // now 05-05 − changedAt 05-02 = 3 days.
    expect(screen.getByText(/3 dagar i steget/)).toBeInTheDocument();
    // The timeline carries the real transition (not an updatedAt synthesis).
    expect(screen.getByText("Status: Utkast → Skickad")).toBeInTheDocument();
  });

  it("shows the latest PAST event as 'Senaste', never a future-scheduled follow-up", () => {
    render(
      <ApplicationDetailBody
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
    render(<ApplicationDetailBody application={makeDetail()} now={NOW} />);
    expect(screen.queryByText(/i steget/)).not.toBeInTheDocument();
    // The created event is still present in the timeline.
    expect(screen.getByText("Ansökan skapades")).toBeInTheDocument();
  });

  // #699 / #1827 B1: the outcome form is reachable wherever the body renders, so a Pending
  // follow-up that the queue flags as overdue can be closed where the queue sends the user.
  it("expands a Pending follow-up row to its outcome form", () => {
    render(
      <ApplicationDetailBody
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
    // #805 punkt 5: sektionsetiketten bär en InfoDialog-"?" (aria-expanded) utanför
    // listan, och kroppen har flera role="list". Hitta uppföljningslistan via dess innehåll.
    const followUpList = screen
      .getAllByRole("list")
      .find((list) => within(list).queryByText("Pingade rekryteraren") != null);
    expect(followUpList).toBeDefined();
    fireEvent.click(
      within(followUpList!).getByRole("button", { expanded: false }),
    );
    expect(screen.getByLabelText("Utfall")).toBeInTheDocument();
  });

  // K3 (Klas 2026-09-26, keep planning): both follow-up affordances render in the body.
  it("offers both logging a follow-up and planning one", () => {
    render(<ApplicationDetailBody application={makeDetail()} now={NOW} />);
    expect(
      screen.getByRole("button", { name: "Logga uppföljning" }),
    ).toBeInTheDocument();
    fireEvent.click(
      screen.getByRole("button", { name: "Planera uppföljning" }),
    );
    expect(screen.getByLabelText(/^Kanal/)).toBeInTheDocument();
  });

  it("shows the earliest follow-up still waiting for its outcome as the next one", () => {
    render(
      <ApplicationDetailBody
        application={makeDetail({
          followUps: [
            {
              id: "f-later",
              channel: "Phone",
              scheduledAt: "2026-05-20T08:00:00Z",
              note: null,
              outcome: "Pending",
              outcomeAt: null,
              createdAt: "2026-05-04T08:00:00Z",
            },
            {
              id: "f-next",
              channel: "Email",
              scheduledAt: "2026-05-12T08:00:00Z",
              note: null,
              outcome: "Pending",
              outcomeAt: null,
              createdAt: "2026-05-04T08:00:00Z",
            },
            {
              id: "f-answered",
              channel: "Email",
              scheduledAt: "2026-05-02T08:00:00Z",
              note: null,
              outcome: "Responded",
              outcomeAt: "2026-05-03T08:00:00Z",
              createdAt: "2026-05-02T08:00:00Z",
            },
          ],
        })}
        now={NOW}
      />,
    );
    const next = screen.getByText(/Nästa uppföljning/);
    expect(next).toHaveTextContent("Nästa uppföljning: 12 maj 2026");
  });

  it("shows no next follow-up when none is waiting for its outcome", () => {
    render(<ApplicationDetailBody application={makeDetail()} now={NOW} />);
    expect(screen.queryByText(/Nästa uppföljning/)).not.toBeInTheDocument();
  });

  it("uses the body's empty follow-up copy", () => {
    render(<ApplicationDetailBody application={makeDetail()} now={NOW} />);
    expect(
      screen.getByText(/Inga uppföljningar ännu/),
    ).toBeInTheDocument();
  });

  it("keeps notes interactive (add-note affordance present)", () => {
    render(<ApplicationDetailBody application={makeDetail()} now={NOW} />);
    expect(
      screen.getByRole("button", { name: "Lägg till anteckning" }),
    ).toBeInTheDocument();
  });

  it("renders the recorded notes", () => {
    render(
      <ApplicationDetailBody
        application={makeDetail({
          notes: [
            {
              id: "n1",
              content: "Ringde rekryteraren",
              createdAt: "2026-05-04T08:00:00Z",
            },
          ],
        })}
        now={NOW}
      />,
    );
    expect(screen.getByText("Ringde rekryteraren")).toBeInTheDocument();
  });

  it("renders the cover letter when there is one, and nothing in its place when there is not", () => {
    const { rerender } = render(
      <ApplicationDetailBody
        application={makeDetail({ coverLetter: "Hej, jag söker tjänsten." })}
        now={NOW}
      />,
    );
    expect(screen.getByText("Personligt brev")).toBeInTheDocument();
    expect(screen.getByText("Hej, jag söker tjänsten.")).toBeInTheDocument();

    rerender(<ApplicationDetailBody application={makeDetail()} now={NOW} />);
    expect(screen.queryByText("Personligt brev")).not.toBeInTheDocument();
  });

  // #805-3: ONE guard (SourceAdSection) decides what the application may claim about the
  // ad, so this pins that the body actually wires it in.
  //
  // Truth-correction: the archived case used to be triggered with `jobAd: null`,
  // a state production never reaches (JobAd.DeletedAt has no writer, #821) — so
  // this test was green while the panel never rendered for a real user. It now
  // triggers on the field production actually writes: `jobAd.status`.
  it("shows the preserved-ad panel ONLY when the source ad is no longer active", () => {
    const { rerender } = render(
      <ApplicationDetailBody
        application={makeDetail({ preservedAd: snapshot })}
        now={NOW}
      />,
    );
    // Ad is live → out-link to the source, no preserved panel.
    expect(
      screen.queryByText("Om annonsen (sparad kopia)"),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("link", {
        name: "Visa annonsen hos Platsbanken (öppnas i ny flik)",
      }),
    ).toHaveAttribute("href", "https://example.com/ad");

    // Ad archived → no link (it would be dead), preserved copy instead.
    rerender(
      <ApplicationDetailBody
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Archived" },
          preservedAd: snapshot,
        })}
        now={NOW}
      />,
    );
    expect(
      screen.getByText("Om annonsen (sparad kopia)"),
    ).toBeInTheDocument();
    expect(screen.getByText("Stockholm")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
  });

  // The body's OWN code in #805-3 is the `application.jobAd ?? null`
  // normalisation (the schema is .nullable().optional()). A cover-letter-only
  // application has no ad row at all — the body must hand `null` to the guard
  // and render no ad surface, rather than crash on an undefined. The guard's own
  // branch matrix is exhausted in source-ad-section.test.tsx; this pins the
  // wiring of the one case the body previously never constructed.
  it("renders no source-ad surface when the application has no ad row at all", () => {
    render(
      <ApplicationDetailBody
        application={makeDetail({
          jobAd: null,
          jobAdId: null,
          preservedAd: null,
        })}
        now={NOW}
      />,
    );
    expect(screen.queryByText("Om annonsen")).not.toBeInTheDocument();
    expect(
      screen.queryByText("Om annonsen (sparad kopia)"),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
    // The rest of the body still renders (the guard degrades, it does not gate).
    expect(screen.getByText("Status")).toBeInTheDocument();
  });

  // #1827 M3 (DESIGN.md §4): a date the user reads is sans with tabular figures, never mono.
  // Every date the body renders is on screen here: the timeline, a follow-up row and its
  // expanded outcome date, a note row, the preserved copy's dates and the next follow-up.
  it("renders its dates in sans: the only mono left is the step picker's caps chip", () => {
    const { container } = render(
      <ApplicationDetailBody
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Archived" },
          preservedAd: { ...snapshot, expiresAt: "2026-05-30T08:00:00Z" },
          followUps: [
            {
              id: "f-answered",
              channel: "Email",
              scheduledAt: "2026-05-02T08:00:00Z",
              note: null,
              outcome: "Responded",
              outcomeAt: "2026-05-03T08:00:00Z",
              createdAt: "2026-05-02T08:00:00Z",
            },
            {
              id: "f-next",
              channel: "Phone",
              scheduledAt: "2026-05-12T08:00:00Z",
              note: null,
              outcome: "Pending",
              outcomeAt: null,
              createdAt: "2026-05-04T08:00:00Z",
            },
          ],
          notes: [
            { id: "n1", content: "Ringde", createdAt: "2026-05-04T08:00:00Z" },
          ],
        })}
        now={NOW}
      />,
    );
    const followUpList = screen
      .getAllByRole("list")
      .find((list) => within(list).queryByText("Svar mottaget") != null);
    fireEvent.click(
      within(followUpList!).getAllByRole("button", { expanded: false })[1]!,
    );
    expect(screen.getByText("(3 maj 2026)")).toBeInTheDocument();

    const mono = [...container.querySelectorAll(".jp-mono, .font-mono")];
    expect(mono.map((el) => el.textContent)).toEqual(["Nu"]);
  });

  // #1827 B4: every section is a region named by its label alone. The "?" help trigger sits
  // beside the label, and a name that swallowed it would read "Uppföljningar Vad är detta? …".
  it("names each section by its label alone", () => {
    render(
      <ApplicationDetailBody
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Archived" },
          preservedAd: snapshot,
          coverLetter: "Hej, jag söker tjänsten.",
        })}
        now={NOW}
      />,
    );
    for (const name of [
      "Uppföljningar",
      "Tidslinje",
      "Anteckningar",
      "Personligt brev",
      "Annonstext",
    ]) {
      expect(screen.getByRole("region", { name })).toBeInTheDocument();
    }
    expect(
      within(screen.getByRole("region", { name: "Annonstext" })).getByText(
        "Sparad annonstext.",
      ),
    ).toBeInTheDocument();
  });

  // #1827: the body gates a terminal move on the saved copy's text. The copy is projected even
  // while the ad is active, so the live-ad case is the one that proves the wiring.
  it("asks before a terminal move when the saved copy has text, even while the ad is live", () => {
    vi.mocked(transitionStatusAction).mockClear();
    render(
      <ApplicationDetailBody
        application={makeDetail({ preservedAd: snapshot })}
        now={NOW}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Nekad" }));

    expect(
      screen.getByRole("dialog", { name: "Markera som Nekad?" }),
    ).toBeInTheDocument();
    expect(transitionStatusAction).not.toHaveBeenCalled();
  });

  it("moves directly when the application has no saved copy", async () => {
    vi.mocked(transitionStatusAction).mockClear();
    render(<ApplicationDetailBody application={makeDetail()} now={NOW} />);
    fireEvent.click(screen.getByRole("button", { name: "Nekad" }));

    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        "Rejected",
      ),
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
