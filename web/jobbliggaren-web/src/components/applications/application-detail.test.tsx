import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApplicationDetail } from "./application-detail";
import type {
  AdSnapshotDto,
  ApplicationDetailDto,
} from "@/lib/types/applications";

// Server-actions mockas (samma repo-mönster som status-edit-card.test) så
// StatusEditCard/AddNoteForm/AddFollowUpForm-öarna kan renderas i jsdom.
vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: vi.fn().mockResolvedValue({ success: true }),
  addNoteAction: vi.fn().mockResolvedValue({ success: true }),
  addFollowUpAction: vi.fn().mockResolvedValue({ success: true }),
  recordFollowUpOutcomeAction: vi.fn().mockResolvedValue({ success: true }),
}));

function makeDetail(
  overrides: Partial<ApplicationDetailDto> = {}
): ApplicationDetailDto {
  return {
    id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01T08:00:00Z",
    updatedAt: "2026-05-10T08:00:00Z",
    jobAd: {
      jobAdId: "ad-1",
      title: "Backend-utvecklare",
      company: "Volvo",
      url: "https://example.com/ad",
      source: "Platsbanken",
      publishedAt: "2026-05-01",
      expiresAt: "2026-06-01",
    },
    coverLetter: null,
    followUps: [],
    notes: [],
    ...overrides,
  };
}

function makeSnapshot(
  overrides: Partial<AdSnapshotDto> = {}
): AdSnapshotDto {
  return {
    title: "Systemutvecklare .NET",
    company: "Spotify",
    location: "Stockholm",
    url: "https://example.com/saved-ad",
    source: "Platsbanken",
    publishedAt: "2026-04-10T08:00:00Z",
    expiresAt: "2026-05-10T08:00:00Z",
    description: "Vi söker en utvecklare med erfarenhet av distribuerade system.",
    capturedAt: "2026-04-12T08:00:00Z",
    ...overrides,
  };
}

describe("ApplicationDetail", () => {
  it("renderar status-block med STATUS_LABELS-etikett (REAL status)", () => {
    render(<ApplicationDetail application={makeDetail()} />);
    // "Status" finns både i status-blocket (label) och StatusEditCard (h2).
    expect(screen.getAllByText("Status").length).toBeGreaterThanOrEqual(2);
    // Submitted → "Skickad" (status-blocket + StatusEditCard-pill).
    expect(screen.getAllByText("Skickad").length).toBeGreaterThan(0);
  });

  it("renderar ApplicationDetail-rubrik när inte headless", () => {
    render(<ApplicationDetail application={makeDetail()} />);
    expect(
      screen.getByRole("heading", { name: "Backend-utvecklare" })
    ).toBeInTheDocument();
  });

  it("utelämnar egen rubrik i headless-läge (modal äger titeln)", () => {
    render(<ApplicationDetail application={makeDetail()} headless />);
    expect(
      screen.queryByRole("heading", { name: "Backend-utvecklare" })
    ).not.toBeInTheDocument();
    // Status-blocket renderas fortfarande.
    expect(screen.getAllByText("Status").length).toBeGreaterThanOrEqual(2);
  });

  it("komponerar tidslinjen av REALA events (skapad + inspelat statusbyte)", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          statusChanges: [
            {
              from: "Draft",
              to: "Submitted",
              changedAt: "2026-05-02T08:00:00Z",
            },
          ],
        })}
      />
    );
    expect(screen.getByText("Tidslinje")).toBeInTheDocument();
    // Native <details> håller barnen i DOM även kollapsad (jsdom renderar inte
    // display:none-dolning) → events resolvar fortfarande via getByText.
    expect(screen.getByText("Ansökan skapades")).toBeInTheDocument();
    // Riktigt inspelat statusbyte → "Status: {från} → {till}" (ADR 0092 D4).
    expect(
      screen.getByText("Status: Utkast → Skickad")
    ).toBeInTheDocument();
  });

  it("fabricerar ALDRIG ett statusbyte ur updatedAt (inga statusChanges → inget statusbyte)", () => {
    // Den pensionerade updatedAt-syntesen (§5, aldrig fabricera en övergång som
    // inte loggats): utan inspelade statusChanges finns INGET "Status:"-event.
    render(<ApplicationDetail application={makeDetail()} />);
    expect(screen.getByText("Ansökan skapades")).toBeInTheDocument();
    expect(screen.queryByText("Status: Skickad")).not.toBeInTheDocument();
  });

  it("tidslinjen är kollapsad som default (<details> utan open-attribut)", () => {
    const { container } = render(
      <ApplicationDetail application={makeDetail()} />
    );
    const details = container.querySelector("details.jp-timeline");
    expect(details).not.toBeNull();
    // Kollapsad = inget `open`-attribut på <details>.
    expect(details?.hasAttribute("open")).toBe(false);
  });

  it("renderar Tidslinje-etiketten som ett <summary>", () => {
    render(<ApplicationDetail application={makeDetail()} />);
    const summary = screen.getByText("Tidslinje").closest("summary");
    expect(summary).not.toBeNull();
  });

  it("renderar real notes[] och utelämnar coverLetter när null", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          notes: [
            {
              id: "n1",
              content: "Ringde rekryteraren",
              createdAt: "2026-05-05T08:00:00Z",
            },
          ],
        })}
      />
    );
    expect(screen.getByText("Ringde rekryteraren")).toBeInTheDocument();
    expect(screen.queryByText("Personligt brev")).not.toBeInTheDocument();
  });

  it("renderar Personligt brev när coverLetter finns", () => {
    render(
      <ApplicationDetail
        application={makeDetail({ coverLetter: "Hej, jag söker tjänsten." })}
      />
    );
    expect(screen.getByText("Personligt brev")).toBeInTheDocument();
    expect(
      screen.getByText("Hej, jag söker tjänsten.")
    ).toBeInTheDocument();
  });

  it("faller tillbaka till mono-id-rubrik när jobAd saknas (manuell)", () => {
    render(
      <ApplicationDetail
        application={makeDetail({ jobAd: null, jobAdId: null })}
      />
    );
    expect(screen.getByText("Ansökan #aaaaaaaa")).toBeInTheDocument();
  });

  // ── #315 (ADR 0086): bevarad annons-snapshot som fallback ──────────────

  it("(a) visar INGEN sparad-kopia-panel när live-annonsen finns", () => {
    render(
      <ApplicationDetail
        application={makeDetail({ preservedAd: makeSnapshot() })}
      />
    );
    // Live-annonsen styr headern (oförändrat beteende).
    expect(
      screen.getByRole("heading", { name: "Backend-utvecklare" })
    ).toBeInTheDocument();
    // Den bevarade panelen renderas EJ när live-annonsen finns (scope:
    // snapshotten är fallback för när annonsen är borta).
    expect(
      screen.queryByText("Om annonsen (sparad kopia)")
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(
        "Vi söker en utvecklare med erfarenhet av distribuerade system."
      )
    ).not.toBeInTheDocument();
  });

  it("(b) live-annons borta + snapshot med text → bevarad titel/företag/not/kropp", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAd: null,
          preservedAd: makeSnapshot(),
        })}
      />
    );
    // Headern bär den BEVARADE titeln (riktig prosa, ej mono-id-fallback).
    expect(
      screen.getByRole("heading", { name: "Systemutvecklare .NET" })
    ).toBeInTheDocument();
    expect(
      screen.queryByText("Ansökan #aaaaaaaa")
    ).not.toBeInTheDocument();
    // Subtitle bär bevarat företag + "sparad kopia"-markör.
    expect(
      screen.getByText(/Spotify · sparad kopia/)
    ).toBeInTheDocument();
    // Panel + lugn "sparad kopia"-not.
    expect(
      screen.getByText("Om annonsen (sparad kopia)")
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Den ursprungliga annonsen finns inte längre/)
    ).toBeInTheDocument();
    // Bevarad metadata (företag + ort i panelen).
    expect(screen.getByText("Ort")).toBeInTheDocument();
    expect(screen.getByText("Stockholm")).toBeInTheDocument();
    // Annonstexten — den enda platsen annonskroppen visas på detaljen.
    expect(screen.getByText("Annonstext")).toBeInTheDocument();
    expect(
      screen.getByText(
        "Vi söker en utvecklare med erfarenhet av distribuerade system."
      )
    ).toBeInTheDocument();
    // Ingen minimerings-not när kroppen finns.
    expect(
      screen.queryByText(/Annonstexten har rensats/)
    ).not.toBeInTheDocument();
  });

  it("(c) live-annons borta + snapshot utan text (terminal) → metadata + minimerings-not, ingen kropp", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAd: null,
          status: "Rejected",
          preservedAd: makeSnapshot({ description: null }),
        })}
      />
    );
    // Titel/företag/metadata visas fortfarande.
    expect(
      screen.getByRole("heading", { name: "Systemutvecklare .NET" })
    ).toBeInTheDocument();
    expect(screen.getByText("Stockholm")).toBeInTheDocument();
    // Annonstext-etiketten finns kvar men kroppen ersätts av en neutral not.
    expect(screen.getByText("Annonstext")).toBeInTheDocument();
    expect(
      screen.getByText(/Annonstexten har rensats eftersom ansökan är avslutad/)
    ).toBeInTheDocument();
    // Ingen tom annonskropp renderas.
    expect(
      screen.queryByText(
        "Vi söker en utvecklare med erfarenhet av distribuerade system."
      )
    ).not.toBeInTheDocument();
  });

  it("(d) preservedAd null (manuell/pre-#315) → oförändrad mono-id-fallback", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAd: null,
          jobAdId: null,
          preservedAd: null,
        })}
      />
    );
    expect(screen.getByText("Ansökan #aaaaaaaa")).toBeInTheDocument();
    expect(
      screen.queryByText("Om annonsen (sparad kopia)")
    ).not.toBeInTheDocument();
  });
});
