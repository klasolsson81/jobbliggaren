import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvPreview } from "./cv-preview";

const PARSED_ID = "11111111-1111-4111-8111-111111111111";

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
    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);

    expect(
      screen.getByRole("button", { name: "Förhandsgranska" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("öppnar modalen vid klick: role=dialog, aria-modal, namn + fetch mot ?profile=Ats", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockResolvedValue(pdfResponse());
    global.fetch = fetchMock;

    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);
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

    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);
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

    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);
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

    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);
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

    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);
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

    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);
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

    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );

    expect(await screen.findByText(/kunde inte laddas/)).toBeInTheDocument();
  });

  it("Stäng-knappen stänger modalen, returnerar fokus till triggern och revokar blob-URL:en", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(pdfResponse());

    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);
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

    render(<CvPreview parsedId={PARSED_ID} initialProfile="Ats" />);
    await user.click(
      screen.getByRole("button", { name: "Förhandsgranska" })
    );
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    await user.keyboard("{Escape}");

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument()
    );
  });
});
