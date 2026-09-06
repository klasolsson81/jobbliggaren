import { describe, it, expect, vi, beforeEach, afterEach, type Mock } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvPreview } from "./cv-preview";

const PARSED_ID = "11111111-1111-4111-8111-111111111111";
const ORIGINAL_URL = `/api/cv/parsed/${PARSED_ID}/original`;
// Den kanoniska ATS-textvyn ges bara för befordrade Resume (Fas 4b PR-8.3).
const RESUME_ID = "22222222-2222-4222-8222-222222222222";
const RESUME_ORIGINAL_URL = `/api/cv/${RESUME_ID}/original`;
const ATS_TEXT_URL = `/api/cv/${RESUME_ID}/ats-text`;
const ATS_TEXT = "Anna Andersson\nBackend-utvecklare\nGöteborg";

const PDF_CONTENT_TYPE = "application/pdf";
const DOCX_CONTENT_TYPE =
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

/** JSON-svar för ATS-textvyn ({ source, text }). En riktig Response duger här —
 *  komponenten läser bara `res.json()` (inte `.blob()`), så body-stream-quirken
 *  som gäller filblobben (se fileResponse) rör inte den här vägen. */
function atsTextResponse(): Response {
  return new Response(JSON.stringify({ source: "Linearized", text: ATS_TEXT }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

/**
 * jsdom implementerar varken URL.createObjectURL / revokeObjectURL eller en
 * riktig PDF-iframe. Vi stubbar objekt-URL-API:erna (komponenten gör/revokar en
 * blob-URL) och mockar fetch per test. Stubbarna restaureras i afterEach.
 *
 * 200-svaret är ett MINIMALT mock-objekt (inte en riktig `Response` runt en
 * `Blob`): komponenten läser bara `ok`, `headers.get("Content-Type")` och
 * `blob()` på happy-path. En äkta `new Response(new Blob(...))` läses tillbaka
 * via `Blob.stream()`, vars tillgänglighet skiljer sig mellan lokal Node och
 * CI:s undici → "object.stream is not a function" i CI. Mock-objektet kringgår
 * body-maskineriet helt och är miljöportabelt. `URL.createObjectURL` är ändå
 * stubbad, så blob-innehållet spelar ingen roll.
 *
 * Content-Type är den enda signal komponenten grenar på — filnamnets ändelse
 * konsulteras aldrig — så den är parametern här.
 */
function fileResponse(contentType: string): Response {
  return {
    ok: true,
    status: 200,
    headers: { get: (name: string) => (name === "Content-Type" ? contentType : null) },
    blob: async () => new Blob(["file"], { type: contentType }),
  } as unknown as Response;
}

/** Fetch-router: ATS-text-URL:en ger JSON, allt annat (originalfilen) ger en PDF. */
function routedFetch(): Mock<typeof fetch> {
  return vi.fn().mockImplementation((url: string) =>
    url.includes("/ats-text") ? atsTextResponse() : fileResponse(PDF_CONTENT_TYPE),
  ) as unknown as Mock<typeof fetch>;
}

/** En kontrollerbar deferred för att hålla fetch pending (loading-state-test). */
function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

describe("<CvPreview /> (originalfilen — Klas-direktiv 2026-09-06)", () => {
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
    render(<CvPreview originalUrl={ORIGINAL_URL} />);

    expect(
      screen.getByRole("button", { name: "Förhandsgranska" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("öppnar modalen vid klick och hämtar ORIGINALET utan ?profile", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE));
    global.fetch = fetchMock;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));

    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveAccessibleName("Förhandsgranskning av CV");

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    // Originalfilen har ingen profilaxel: URL:en bär ingen query alls.
    expect(url).toBe(ORIGINAL_URL);
  });

  it("loading-state: 'Filen läses in…' visas medan fetch är pending", async () => {
    const user = userEvent.setup();
    const pending = deferred<Response>();
    global.fetch = vi.fn(() => pending.promise) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));

    // BrandSpinner-status renderar "Filen läses in…" (sr-only + aria-hidden p).
    expect(await screen.findAllByText("Filen läses in…")).not.toHaveLength(0);
    expect(screen.queryByTitle("Din uppladdade originalfil")).not.toBeInTheDocument();

    // Lös upp så in-flight-fetchen inte läcker in i nästa test.
    pending.resolve(fileResponse(PDF_CONTENT_TYPE));
    await screen.findByTitle("Din uppladdade originalfil");
  });

  it("pdf: iframe + 'Öppna i ny flik' + nedladdningslänk + createObjectURL anropad", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));

    const frame = await screen.findByTitle("Din uppladdade originalfil");
    expect(frame).toHaveAttribute("src", "blob:mock");
    expect(createObjectURL).toHaveBeenCalledTimes(1);

    expect(screen.getByRole("link", { name: "Öppna i ny flik" })).toHaveAttribute(
      "href",
      "blob:mock"
    );
    // Nedladdningen gör det integritetsmeddelandet redan lovar: att du kan hämta
    // tillbaka din egen fil (content-legal.json, "originalfil").
    const download = screen.getByRole("link", { name: "Ladda ner originalfilen" });
    expect(download).toHaveAttribute("download", "original.pdf");
  });

  it("docx: INGEN iframe, utan civil förklaring + nedladdningslänk", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(DOCX_CONTENT_TYPE)) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));

    // En iframe hade gett en TYST tom ram — därför finns den inte alls här.
    expect(
      await screen.findByText(/Word-dokument och kan inte visas i webbläsaren/)
    ).toBeInTheDocument();
    expect(screen.queryByTitle("Din uppladdade originalfil")).not.toBeInTheDocument();

    const download = screen.getByRole("link", { name: "Ladda ner originalfilen" });
    expect(download).toHaveAttribute("download", "original.docx");
  });

  it("okänd content-type → fel-copy, aldrig en ram med okänt innehåll", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue(fileResponse("text/html")) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));

    expect(
      await screen.findByText("Originalfilen kunde inte laddas. Försök igen om en stund.")
    ).toBeInTheDocument();
    expect(screen.queryByTitle("Din uppladdade originalfil")).not.toBeInTheDocument();
  });

  it("404 → ärligt tomt tillstånd, inte ett fel", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue({ ok: false, status: 404 } as Response) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));

    // Ett CV skapat i tjänsten, eller en import där filen aldrig sparades, HAR
    // ingen originalfil. Det är ett vanligt utfall och sägs som ett sådant.
    expect(
      await screen.findByText(
        "Vi har ingen originalfil sparad för det här CV:t. Importera ditt CV på nytt om du vill kunna öppna filen här."
      )
    ).toBeInTheDocument();
    expect(screen.queryByTitle("Din uppladdade originalfil")).not.toBeInTheDocument();
  });

  it("429 → civic copy med '30 sekunder', ingen iframe", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 429,
      json: async () => ({ error: "rateLimited", retryAfterSeconds: 30 }),
    } as unknown as Response) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));

    expect(await screen.findByText(/30 sekunder/)).toBeInTheDocument();
    expect(screen.queryByTitle("Din uppladdade originalfil")).not.toBeInTheDocument();
  });

  it("övrigt fel (500) → civic copy 'kunde inte laddas'", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue({ ok: false, status: 500 } as Response) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));

    expect(
      await screen.findByText("Originalfilen kunde inte laddas. Försök igen om en stund.")
    ).toBeInTheDocument();
  });

  it("Stäng-knappen stänger modalen, returnerar fokus till triggern och revokar blob-URL:en", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    const trigger = screen.getByRole("button", { name: "Förhandsgranska" });
    await user.click(trigger);
    await screen.findByTitle("Din uppladdade originalfil");

    await user.click(screen.getByRole("button", { name: "Stäng" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:mock");
  });

  it("Esc stänger modalen", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));
    await screen.findByTitle("Din uppladdade originalfil");

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  describe("vyval", () => {
    it("visar INGEN flikrad när atsTextUrl saknas (parsat CV)", async () => {
      const user = userEvent.setup();
      global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

      render(<CvPreview originalUrl={ORIGINAL_URL} />);
      await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));
      await screen.findByTitle("Din uppladdade originalfil");

      // En ensam flik är en kontroll som inte kontrollerar något.
      expect(screen.queryByRole("group", { name: "Välj vy" })).not.toBeInTheDocument();
      expect(
        screen.queryByRole("button", { name: "Textversion för ATS" })
      ).not.toBeInTheDocument();
    });

    it("profilflikarna ATS-profil/Visuell profil finns INTE längre", async () => {
      const user = userEvent.setup();
      global.fetch = routedFetch() as unknown as typeof fetch;

      render(<CvPreview originalUrl={RESUME_ORIGINAL_URL} atsTextUrl={ATS_TEXT_URL} />);
      await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));
      await screen.findByTitle("Din uppladdade originalfil");

      // Originalet har ingen ATS-variant och ingen visuell variant — det är en fil.
      expect(screen.queryByRole("button", { name: "ATS-profil" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Visuell profil" })).not.toBeInTheDocument();
    });

    it("visar flikarna Originalfil + Textversion för ATS när atsTextUrl ges", async () => {
      const user = userEvent.setup();
      global.fetch = routedFetch() as unknown as typeof fetch;

      render(<CvPreview originalUrl={RESUME_ORIGINAL_URL} atsTextUrl={ATS_TEXT_URL} />);
      await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));

      expect(screen.getByRole("group", { name: "Välj vy" })).toBeInTheDocument();
      const original = screen.getByRole("button", { name: "Originalfil" });
      expect(original).toHaveAttribute("aria-current", "true");
      expect(screen.getByRole("button", { name: "Textversion för ATS" })).toBeInTheDocument();
    });

    it("aktivering hämtar atsTextUrl och renderar texten + banner-copyn", async () => {
      const user = userEvent.setup();
      const fetchMock = routedFetch();
      global.fetch = fetchMock as unknown as typeof fetch;

      render(<CvPreview originalUrl={RESUME_ORIGINAL_URL} atsTextUrl={ATS_TEXT_URL} />);
      await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));
      await screen.findByTitle("Din uppladdade originalfil");

      await user.click(screen.getByRole("button", { name: "Textversion för ATS" }));

      expect(await screen.findByText(/Så här läser en ATS-parser ditt CV/)).toBeInTheDocument();
      expect(screen.getByText(/Backend-utvecklare/)).toBeInTheDocument();
      // Textvyn ersätter ramen — de två är olika dokument, aldrig två vyer av ett.
      expect(screen.queryByTitle("Din uppladdade originalfil")).not.toBeInTheDocument();

      const atsCalls = fetchMock.mock.calls.filter(([url]) =>
        String(url).includes("/ats-text")
      );
      expect(atsCalls).toHaveLength(1);
    });

    it("404 på ats-text → civic copy 'Textversionen är inte tillgänglig ännu.'", async () => {
      const user = userEvent.setup();
      global.fetch = vi.fn().mockImplementation((url: string) =>
        url.includes("/ats-text")
          ? ({ ok: false, status: 404 } as Response)
          : fileResponse(PDF_CONTENT_TYPE)
      ) as unknown as typeof fetch;

      render(<CvPreview originalUrl={RESUME_ORIGINAL_URL} atsTextUrl={ATS_TEXT_URL} />);
      await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));
      await screen.findByTitle("Din uppladdade originalfil");

      await user.click(screen.getByRole("button", { name: "Textversion för ATS" }));

      expect(
        await screen.findByText("Textversionen är inte tillgänglig ännu.")
      ).toBeInTheDocument();
    });

    it("byte tillbaka till Originalfil återställer ramen", async () => {
      const user = userEvent.setup();
      global.fetch = routedFetch() as unknown as typeof fetch;

      render(<CvPreview originalUrl={RESUME_ORIGINAL_URL} atsTextUrl={ATS_TEXT_URL} />);
      await user.click(screen.getByRole("button", { name: "Förhandsgranska" }));
      await screen.findByTitle("Din uppladdade originalfil");

      await user.click(screen.getByRole("button", { name: "Textversion för ATS" }));
      await screen.findByText(/Så här läser en ATS-parser ditt CV/);

      await user.click(screen.getByRole("button", { name: "Originalfil" }));

      expect(await screen.findByTitle("Din uppladdade originalfil")).toBeInTheDocument();
    });
  });
});
