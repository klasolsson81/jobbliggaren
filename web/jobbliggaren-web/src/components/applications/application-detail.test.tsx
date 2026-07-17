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
      // #805-3: en JobAd-länkad ansökan bär ALLTID en status. "Active" är
      // prod-normalfallet (annonsen ligger uppe hos källan) → utlänken visas.
      status: "Active",
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
    contacts: [],
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

  // ── #315 (ADR 0086) + #805-3: bevarad annons-snapshot som fallback ─────
  //
  // #805-3 SANNINGSKORRIGERING av denna svit: testerna nedan triggade tidigare
  // den bevarade panelen med `jobAd: null` — ett tillstånd produktionen ALDRIG
  // når för en JobAd-länkad ansökan (JobAd.DeletedAt saknar writer, #821), så
  // panelen renderades aldrig i verkligheten trots att sviten var grön. Den
  // falska tryggheten är en del av rotorsaken. Borta-läget triggas nu på det
  // fältet produktionen faktiskt skriver: `jobAd.status !== "Active"`.

  it("(a) live-annons → INGEN sparad-kopia-panel, MEN en utlänk till källan", () => {
    render(
      <ApplicationDetail
        application={makeDetail({ preservedAd: makeSnapshot() })}
      />
    );
    // Live-annonsen styr headern (oförändrat beteende).
    expect(
      screen.getByRole("heading", { name: "Backend-utvecklare" })
    ).toBeInTheDocument();
    // Den bevarade panelen renderas EJ medan annonsen är aktiv (snapshotten är
    // borta-lägets fallback, inte en dubblett av live-annonsen).
    expect(
      screen.queryByText("Om annonsen (sparad kopia)")
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(
        "Vi söker en utvecklare med erfarenhet av distribuerade system."
      )
    ).not.toBeInTheDocument();
    // #805-3: utlänken till KÄLLANS annons (Beslut B) — pekar på live-annonsens
    // url, inte snapshottens, och öppnas säkert i ny flik.
    const link = screen.getByRole("link", {
      name: "Visa annonsen hos Platsbanken (öppnas i ny flik)",
    });
    expect(link).toHaveAttribute("href", "https://example.com/ad");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("(b) annons ARKIVERAD + snapshot med text → ingen länk, bevarad kopia visas", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Archived" },
          preservedAd: makeSnapshot(),
        })}
      />
    );
    // Headern bär fortfarande annonsens titel — arkivering är inte radering,
    // raden joinar kvar. (Före #805-3 föll headern tillbaka på snapshot-titeln,
    // men bara i det fabricerade jobAd == null-fallet.)
    expect(
      screen.getByRole("heading", { name: "Backend-utvecklare" })
    ).toBeInTheDocument();
    // INGEN utlänk: annonsen är inte längre aktiv hos källan, så vi kan inte
    // hävda att URL:en svarar. Beslut B: "ingen död länk".
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
    // Panel + lugn "sparad kopia"-not.
    expect(
      screen.getByText("Om annonsen (sparad kopia)")
    ).toBeInTheDocument();
    expect(
      // m5 (code-reviewer) + M2 (design-reviewer): copy:n sade tidigare att
      // annonsen "finns inte längre" — men panelen renderas för VARJE icke-Active
      // status, och en arkiverad annons kan mycket väl finnas kvar hos källan.
      // Vi påstår nu bara det vi vet: den är inte längre aktiv.
      screen.getByText(/Annonsen är inte längre aktiv/)
    ).toBeInTheDocument();
    // Bevarad metadata (ort i panelen).
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

  it("(c) annons ARKIVERAD + snapshot utan text (terminal) → metadata + minimerings-not, ingen kropp", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Archived" },
          status: "Rejected",
          preservedAd: makeSnapshot({ description: null }),
        })}
      />
    );
    // Bevarad metadata visas fortfarande (retention-minimeringen tar bara kroppen).
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

  it("(d) ingen annonsrad alls (enbart brev) → mono-id-fallback, ingen annons-yta", () => {
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
    expect(screen.queryByText("Om annonsen")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
  });

  // ── #805-3 (Beslut B): "Visa annonsen" — utlänk till källan ────────────

  it("(e) RADERAD annons (Erased) behandlas som borta — ingen länk", () => {
    // Domänen har TRE statusvärden (Active | Archived | Erased — det
    // writerlösa Expired retirerades i #886), och Art. 17-tombstonens status
    // når den här ytan på riktigt (lös z.string()-typning, ingen Erased-mask
    // på ansöknings-läsvägen). Liveness hävdas bara på positivt "Active"
    // (default-deny); den naiva inversen (!== "Archived" ⇒ live) hade skeppat
    // en länk till en RADERAD annons här.
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Erased" },
          preservedAd: makeSnapshot(),
        })}
      />
    );
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
    expect(
      screen.getByText("Om annonsen (sparad kopia)")
    ).toBeInTheDocument();
  });

  it("(f) annons borta UTAN bevarad kopia (pre-#315) → lugn not, ingen länk", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Archived" },
          preservedAd: null,
        })}
      />
    );
    expect(
      screen.getByText("Annonsen är inte längre aktiv hos Platsbanken.")
    ).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
  });

  it("(g) MANUELL ansökan med sparad url → länk utan källa-påstående", () => {
    // Ingen JobAd-rad ⇒ ingen arkivering ⇒ vi kan inte hävda live ELLER borta.
    // Vi visar länken användaren själv sparade och påstår ingenting om den.
    // aria-label:n utelämnar källan — annars: "Visa annonsen hos Manuellt".
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAdId: null,
          jobAd: {
            jobAdId: null,
            title: "Manuell titel",
            company: "Manuellt företag",
            url: "https://example.com/manuell",
            source: "Manual",
            publishedAt: null,
            expiresAt: null,
            status: null,
          },
          preservedAd: null,
        })}
      />
    );
    const link = screen.getByRole("link", {
      name: "Visa annonsen (öppnas i ny flik)",
    });
    expect(link).toHaveAttribute("href", "https://example.com/manuell");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    // Ingen borta-not — vi gör ingen livs-utsaga för manuella.
    expect(screen.queryByText(/inte längre aktiv/)).toBeNull();
  });

  it("(h) manuell ansökan UTAN url → ingen länk, ingen tom sektion", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAdId: null,
          jobAd: {
            jobAdId: null,
            title: "Manuell titel",
            company: "Manuellt företag",
            url: null,
            source: "Manual",
            publishedAt: null,
            expiresAt: null,
            status: null,
          },
          preservedAd: null,
        })}
      />
    );
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
    expect(screen.queryByText("Om annonsen")).not.toBeInTheDocument();
  });

  it("(i) deploy-skew: status saknas i svaret → ingen länk (default-deny)", () => {
    // Äldre/cachead BE-respons utan status-fältet. Vi vet inte om annonsen
    // lever → vi hävdar ingenting och länkar inte. Aldrig en gissad länk.
    const { jobAd } = makeDetail();
    const { status: _omitted, ...withoutStatus } = jobAd!;
    render(
      <ApplicationDetail
        application={makeDetail({ jobAd: withoutStatus, preservedAd: null })}
      />
    );
    expect(screen.queryByRole("link", { name: /Visa annonsen/ })).toBeNull();
    // …och ingen BORTA-utsaga heller: "Annonsen är inte längre aktiv" vore lika
    // falskt som en död länk — vi vet inte att den är borta. Utan denna assert
    // passerar en guard som läser borta-läget som `status !== "Active"` (utan
    // null-villkoret), och skew:en skulle då tala om för användaren att en
    // annons som mycket väl ligger uppe är död. Uttömmande grentäckning för
    // detta tillstånd bor i source-ad-section.test.tsx (guarden själv).
    expect(screen.queryByText(/inte längre aktiv/)).toBeNull();
  });

  // ── #892 (CTO R1): borttagen-markören i fullsidans header ───────────────
  //
  // BE-fallbacken ger headern den bevarade (frysta) identiteten för en raderad
  // annons — som utan markör renderas IDENTISKT med en levande annons. Markören
  // är andra halvan av samma fix (R1): bevarad identitet utan dödssignal låter
  // en död annons se levande ut. Denna gren (`{adRemoved && …}` i jp-modal__head)
  // var otäckt — test (e) renderar en Erased-annons men bevisar bara kroppens
  // SourceAdSection, aldrig header-markören.

  it("(j) RADERAD annons (Erased) → header-markör 'Annonsen är borttagen' + bevarad identitet", () => {
    render(
      <ApplicationDetail
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Erased" },
          preservedAd: makeSnapshot(),
        })}
      />
    );
    // Headern bär den bevarade identiteten (BE-fallbacken) …
    expect(
      screen.getByRole("heading", { name: "Backend-utvecklare" })
    ).toBeInTheDocument();
    // … och dödssignalen så den inte ser levande ut. Distinkt copy från kroppens
    // "Om annonsen (sparad kopia)"/"inte längre aktiv" — ingen kollision.
    const marker = screen.getByText("Annonsen är borttagen");
    expect(marker).toHaveClass("jp-tag");
  });

  it("(k) header-markören uteblir för arkiverad OCH levande annons (Erased-exakt)", () => {
    const { rerender } = render(
      <ApplicationDetail
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Archived" },
          preservedAd: makeSnapshot(),
        })}
      />
    );
    // Arkiverad ≠ raderad: raden lever, ingen dödssignal i headern.
    expect(screen.queryByText("Annonsen är borttagen")).toBeNull();

    rerender(<ApplicationDetail application={makeDetail()} />);
    expect(screen.queryByText("Annonsen är borttagen")).toBeNull();
  });

  it("(l) headless-läge → ingen header-markör (modalen äger headern, SPOT)", () => {
    // I headless-läge utelämnas hela jp-modal__head — markören bärs då av
    // route-modalens subtitle (@modal-page-testet), inte här. En dubblerad
    // markör vore drift (två ytor eniga om VAD men oeniga om VAR, SPOT).
    render(
      <ApplicationDetail
        headless
        application={makeDetail({
          jobAd: { ...makeDetail().jobAd!, status: "Erased" },
          preservedAd: makeSnapshot(),
        })}
      />
    );
    expect(screen.queryByText("Annonsen är borttagen")).toBeNull();
    // Kroppen renderas fortfarande (borta-läget ägs av SourceAdSection).
    expect(
      screen.getByText("Om annonsen (sparad kopia)")
    ).toBeInTheDocument();
  });
});
