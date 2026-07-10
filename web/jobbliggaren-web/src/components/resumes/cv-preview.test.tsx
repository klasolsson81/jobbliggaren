import { describe, it, expect, vi, beforeEach, afterEach, type Mock } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvPreview } from "./cv-preview";

const PARSED_ID = "11111111-1111-4111-8111-111111111111";
const PREVIEW_URL = `/api/cv/parsed/${PARSED_ID}/preview`;
// Den kanoniska ATS-textvyn ges bara för befordrade Resume (Fas 4b PR-8.3).
const RESUME_ID = "22222222-2222-4222-8222-222222222222";
const RESUME_PREVIEW_URL = `/api/cv/${RESUME_ID}/preview`;
const ATS_TEXT_URL = `/api/cv/${RESUME_ID}/ats-text`;
const ATS_TEXT = "Anna Andersson\nBackend-utvecklare\nGöteborg";

/** JSON-svar för ATS-textvyn ({ source, text }). En riktig Response duger här —
 *  komponenten läser bara `res.json()` (inte `.blob()`), så body-stream-quirken
 *  som gäller PDF-blobben (se pdfResponse) rör inte den här vägen. */
function atsTextResponse(): Response {
  return new Response(JSON.stringify({ source: "Linearized", text: ATS_TEXT }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

/** Fetch-router: ATS-text-URL:en ger JSON, allt annat (preview) ger en PDF-blob.
 * `Mock<typeof fetch>` är både anropbar med fetch-signaturen (tilldelningen till
 * `global.fetch` typar rent) och bär `.mock.calls` typade som fetch-parametrar. */
function routedFetch(): Mock<typeof fetch> {
  return vi.fn().mockImplementation((url: string) =>
    url.includes("/ats-text") ? atsTextResponse() : pdfResponse(),
  ) as unknown as Mock<typeof fetch>;
}

/**
 * jsdom implementerar varken URL.createObjectURL / revokeObjectURL eller en
 * riktig PDF-iframe. Vi stubbar objekt-URL-API:erna (komponenten gör/revokar en
 * blob-URL) och mockar fetch per test. Stubbarna restaureras i afterEach.
 *
 * 200-svaret är ett MINIMALT mock-objekt (inte en riktig `Response` runt en
 * `Blob`): komponenten läser bara `ok` + `blob()` på happy-path. En äkta
 * `new Response(new Blob(...))` läses tillbaka via `Blob.stream()`, vars
 * tillgänglighet skiljer sig mellan lokal Node och CI:s undici → "object.stream
 * is not a function" i CI. Mock-objektet kringgår body-maskineriet helt och är
 * miljöportabelt. `URL.createObjectURL` är ändå stubbad, så blob-innehållet
 * spelar ingen roll.
 */
function pdfResponse(): Response {
  return {
    ok: true,
    status: 200,
    blob: async () => new Blob(["pdf"], { type: "application/pdf" }),
  } as unknown as Response;
}

/** En kontrollerbar deferred för att hålla fetch pending (loading-state-test). */
function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

describe("<CvPreview /> (Fas 4 STEG B-2 — Förhandsgranska CV)", () => {
  const originalFetch = global.fetch;
  const originalCreate = URL.createObjectURL;
  const originalRevoke = URL.revokeObjectURL;
  let createObjectURL: ReturnType<typeof vi.fn>;
  let revokeObjectURL: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createObjectURL = vi.fn(() => "blob:mock");
    revokeObjectURL = vi.fn();
    URL.createObjectURL = createObjectURL as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = revokeObjectURL as unknown as typeof URL.revokeObjectURL;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    URL.createObjectURL = originalCreate;
    URL.revokeObjectURL = originalRevoke;
    vi.restoreAllMocks();
  });

  it("renderar trigger-knappen 'Förhandsgranska' och visar INTE modalen initialt", () => {
    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);

    expect(
      screen.getByRole("button", { name: "Förhandsgranska" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("öppnar modalen vid klick: role=dialog, aria-modal, namn + fetch mot ?profile=Ats", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockResolvedValue(pdfResponse());
    global.fetch = fetchMock;

    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );

    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveAccessibleName("Förhandsgranskning av CV");

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`/api/cv/parsed/${PARSED_ID}/preview?profile=Ats`);
  });

  it("loading-state: 'CV:t läses in…' visas medan fetch är pending", async () => {
    const user = userEvent.setup();
    const d = deferred<Response>();
    global.fetch = vi.fn().mockReturnValue(d.promise);

    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );

    // BrandSpinner-status renderar "CV:t läses in…" (sr-only + aria-hidden p).
    expect(
      await screen.findAllByText("CV:t läses in…")
    ).not.toHaveLength(0);

    // Lös upp så in-flight-fetchen inte läcker in i nästa test.
    d.resolve(pdfResponse());
    await screen.findByTitle("Förhandsgranskning av CV (ATS-profil)");
  });

  it("ready-state: iframe + 'Öppna i ny flik'-länk + createObjectURL anropad (Ats)", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(pdfResponse());

    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );

    const iframe = await screen.findByTitle(
      "Förhandsgranskning av CV (ATS-profil)"
    );
    expect(iframe).toBeInTheDocument();
    expect(createObjectURL).toHaveBeenCalled();

    const link = screen.getByRole("link", { name: "Öppna i ny flik" });
    expect(link).toHaveAttribute(
      "href",
      `/api/cv/parsed/${PARSED_ID}/preview?profile=Ats`
    );
  });

  it("profil-byte: klick på 'Visuell profil' hämtar om med ?profile=Visual och byter iframe-titel", async () => {
    const user = userEvent.setup();
    // Färsk Response per anrop: en Response-body kan bara läsas (.blob()) en gång,
    // och komponenten gör två separata fetchar (Ats → Visual). Ett delat
    // Response-objekt skulle ge "body already used" vid andra anropet.
    const fetchMock = vi.fn().mockImplementation(() => pdfResponse());
    global.fetch = fetchMock;

    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );
    await screen.findByTitle("Förhandsgranskning av CV (ATS-profil)");

    await user.click(screen.getByRole("button", { name: "Visuell profil" }));

    await screen.findByTitle("Förhandsgranskning av CV (Visuell profil)");
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const [secondUrl] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(secondUrl).toBe(
      `/api/cv/parsed/${PARSED_ID}/preview?profile=Visual`
    );
  });

  it("429 → civic copy med '30 sekunder', ingen iframe", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ retryAfterSeconds: 30 }), {
        status: 429,
        headers: { "Content-Type": "application/json" },
      })
    );

    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );

    expect(await screen.findByText(/30 sekunder/)).toBeInTheDocument();
    expect(
      screen.queryByTitle("Förhandsgranskning av CV (ATS-profil)")
    ).not.toBeInTheDocument();
  });

  it("404 → civic copy 'kunde inte hittas'", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 404 }));

    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );

    expect(await screen.findByText(/kunde inte hittas/)).toBeInTheDocument();
  });

  it("övrigt fel (500) → civic copy 'kunde inte laddas'", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 500 }));

    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );

    expect(await screen.findByText(/kunde inte laddas/)).toBeInTheDocument();
  });

  it("Stäng-knappen stänger modalen, returnerar fokus till triggern och revokar blob-URL:en", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(pdfResponse());

    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
    const trigger = screen.getByRole("button", { name: "Förhandsgranska" });
    await user.click(trigger);
    // Vänta till ready-state så en blob-URL faktiskt finns att revoka.
    await screen.findByTitle("Förhandsgranskning av CV (ATS-profil)");

    await user.click(screen.getByRole("button", { name: "Stäng" }));

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument()
    );
    expect(trigger).toHaveFocus();
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:mock");
  });

  it("Esc stänger modalen", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(pdfResponse());

    render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    await user.keyboard("{Escape}");

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument()
    );
  });

  // Textversion för ATS (Fas 4b PR-8.3): tredje fliken visas bara när atsTextUrl ges.
  describe("ATS-textflik", () => {
    it("visar INTE fliken 'Textversion för ATS' när atsTextUrl saknas (parsat CV)", async () => {
      const user = userEvent.setup();
      global.fetch = vi.fn().mockResolvedValue(pdfResponse());

      render(<CvPreview previewUrl={PREVIEW_URL} initialProfile="Ats" />);
      await user.click(
        screen.getByRole("button", { name: "Förhandsgranska" })
      );

      expect(
        screen.queryByRole("button", { name: "Textversion för ATS" })
      ).not.toBeInTheDocument();
    });

    it("visar fliken 'Textversion för ATS' när atsTextUrl ges (befordrat CV)", async () => {
      const user = userEvent.setup();
      global.fetch = routedFetch();

      render(
        <CvPreview
          previewUrl={RESUME_PREVIEW_URL}
          atsTextUrl={ATS_TEXT_URL}
          initialProfile="Ats"
        />
      );
      await user.click(
        screen.getByRole("button", { name: "Förhandsgranska" })
      );

      expect(
        screen.getByRole("button", { name: "Textversion för ATS" })
      ).toBeInTheDocument();
    });

    it("aktivering hämtar atsTextUrl och renderar texten + banner-copyn", async () => {
      const user = userEvent.setup();
      const fetchMock = routedFetch();
      global.fetch = fetchMock;

      render(
        <CvPreview
          previewUrl={RESUME_PREVIEW_URL}
          atsTextUrl={ATS_TEXT_URL}
          initialProfile="Ats"
        />
      );
      await user.click(
        screen.getByRole("button", { name: "Förhandsgranska" })
      );
      await user.click(
        screen.getByRole("button", { name: "Textversion för ATS" })
      );

      // Banner-copyn (ATS läser en spalt, ren text) + den linjäriserade texten.
      expect(
        await screen.findByText(/Så här läser en ATS-parser ditt CV/)
      ).toBeInTheDocument();
      expect(screen.getByText(/Backend-utvecklare/)).toBeInTheDocument();

      // ATS-textfliken körde en GET mot ats-text-URL:en.
      const atsCall = fetchMock.mock.calls.find(([url]) =>
        String(url).includes("/ats-text")
      );
      expect(atsCall?.[0]).toBe(ATS_TEXT_URL);
      // Iframe:n (PDF) är borta när textfliken är aktiv.
      expect(
        screen.queryByTitle("Förhandsgranskning av CV (ATS-profil)")
      ).not.toBeInTheDocument();
    });

    it("404 på ats-text → civic copy 'Textversionen är inte tillgänglig ännu.'", async () => {
      const user = userEvent.setup();
      global.fetch = vi.fn().mockImplementation((url: string) =>
        url.includes("/ats-text")
          ? new Response(null, { status: 404 })
          : pdfResponse()
      );

      render(
        <CvPreview
          previewUrl={RESUME_PREVIEW_URL}
          atsTextUrl={ATS_TEXT_URL}
          initialProfile="Ats"
        />
      );
      await user.click(
        screen.getByRole("button", { name: "Förhandsgranska" })
      );
      await user.click(
        screen.getByRole("button", { name: "Textversion för ATS" })
      );

      expect(
        await screen.findByText("Textversionen är inte tillgänglig ännu.")
      ).toBeInTheDocument();
    });

    it("byte tillbaka till en PDF-profil återställer iframe:n", async () => {
      const user = userEvent.setup();
      global.fetch = routedFetch();

      render(
        <CvPreview
          previewUrl={RESUME_PREVIEW_URL}
          atsTextUrl={ATS_TEXT_URL}
          initialProfile="Ats"
        />
      );
      await user.click(
        screen.getByRole("button", { name: "Förhandsgranska" })
      );
      // PDF-iframe initialt (Ats).
      await screen.findByTitle("Förhandsgranskning av CV (ATS-profil)");

      // Till textfliken → iframe borta.
      await user.click(
        screen.getByRole("button", { name: "Textversion för ATS" })
      );
      await screen.findByText(/Så här läser en ATS-parser ditt CV/);
      expect(
        screen.queryByTitle("Förhandsgranskning av CV (ATS-profil)")
      ).not.toBeInTheDocument();

      // Tillbaka till ATS-profil → PDF-iframe återställd.
      await user.click(screen.getByRole("button", { name: "ATS-profil" }));
      expect(
        await screen.findByTitle("Förhandsgranskning av CV (ATS-profil)")
      ).toBeInTheDocument();
    });
  });
});
