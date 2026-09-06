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
 * jsdom implementerar inte URL.createObjectURL / revokeObjectURL. Vi stubbar dem
 * (komponenten gör/revokar en blob-URL för nedladdningslänken) och mockar fetch per
 * test. Stubbarna restaureras i afterEach.
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

  it("renderar trigger-knappen 'Ladda ner CV-filen' och visar INTE modalen initialt", () => {
    render(<CvPreview originalUrl={ORIGINAL_URL} />);

    expect(
      screen.getByRole("button", { name: "Ladda ner CV-filen" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("öppnar modalen vid klick och hämtar ORIGINALET utan ?profile", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE));
    global.fetch = fetchMock;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveAccessibleName("Din CV-fil");

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
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    // BrandSpinner-status renderar "Filen läses in…" (sr-only + aria-hidden p).
    expect(await screen.findAllByText("Filen läses in…")).not.toHaveLength(0);
    expect(screen.queryByRole("link", { name: "Ladda ner" })).not.toBeInTheDocument();

    // Lös upp så in-flight-fetchen inte läcker in i nästa test.
    pending.resolve(fileResponse(PDF_CONTENT_TYPE));
    await screen.findByRole("link", { name: "Ladda ner" });
  });

  it("pdf: nedladdningslänk med rätt ändelse, och INGEN renderande yta", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    // Nedladdningen gör det integritetsmeddelandet redan lovar: att du kan hämta
    // tillbaka din egen fil (content-legal.json, "originalfil").
    const download = await screen.findByRole("link", { name: "Ladda ner" });
    expect(download).toHaveAttribute("href", "blob:mock");
    expect(download).toHaveAttribute("download", "original.pdf");
    expect(createObjectURL).toHaveBeenCalledTimes(1);
    expect(screen.getByText(/Den visas inte i webbläsaren av säkerhetsskäl/)).toBeInTheDocument();
  });

  it("GDPR-grinden: användarens bytes renderas ALDRIG inline på vår origin", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));
    await screen.findByRole("link", { name: "Ladda ner" });

    // DPIA #659 M-F2 föreskriver RFC 6266 `attachment` ordagrant och är merge-blockerande;
    // R-F6:s residual vilar på att en lagrad polyglot ALDRIG renderas inline från vår origin,
    // och ADR 0101 §B5(a):s GO för hela resume_files-lagret är villkorat av M-F2.
    //
    // Pinnen sitter på FRÅNVARON AV EN RENDERANDE YTA, inte på svarshuvudet: `fetch()` läser
    // aldrig `Content-Disposition`, så en kvarlämnad iframe hade renderat vidare medan headern
    // såg efterlevande ut. Att bara mäta headern hade varit en halv mätning av en hel grind.
    // Mätt mot `document`, inte mot `container`: den positiva kontrollen nedan söker i
    // document, och om modalen någon gång portas till document.body blir en
    // container-scopad negation grön VAKUÖST medan kontrollen fortfarande hittar länken.
    const root = document.body;
    expect(root.querySelector("iframe")).toBeNull();
    expect(root.querySelector("embed")).toBeNull();
    expect(root.querySelector("object")).toBeNull();
    // SCOPE-KONTROLL: sökroten innehåller faktiskt det den ska mäta. Utan den kan de tre
    // null-assertionerna ovan inte skiljas från "letade på fel ställe".
    expect(root.querySelectorAll("a[download]").length).toBe(1);
    // Ingen länk i dialogen navigerar i stället för att spara, och ingen öppnar ny flik.
    for (const link of Array.from(root.querySelectorAll("a[href]"))) {
      const href = link.getAttribute("href") ?? "";
      if (href.startsWith("blob:") || href.startsWith("data:")) {
        expect(link).toHaveAttribute("download");
      }
      expect(link).not.toHaveAttribute("target", "_blank");
    }
  });

  it("ingen väg öppnar blob:en programmatiskt — window.open rörs aldrig", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    const open = vi.spyOn(window, "open").mockImplementation(() => null);

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));
    await screen.findByRole("link", { name: "Ladda ner" });

    // Pinnen ovan mäter DOM-noder. En knapp med onClick={() => window.open(url)} hade
    // passerat den utan att lämna en enda nod — och ändå målat användarens bytes på vår
    // origin. Den här mäter beteendet i stället för formen.
    const dialog = screen.getByRole("dialog");
    const closeButton = screen.getByRole("button", { name: "Stäng" });
    // Stäng utesluts MEDVETET: den är först i dokumentordning, och att klicka den
    // avmonterar dialogen. En tidigare version av det här testet gjorde exakt det och
    // bröt sedan ur loopen, så den mätte bara stäng-knappen — en NO-OP hade gett samma
    // gröna.
    const controls = Array.from(dialog.querySelectorAll("button, a")).filter(
      (el) => el !== closeButton
    );

    // POSITIV KONTROLL: nedladdningslänken är faktiskt med bland elementen som klickas.
    // Utan den återinför nästa omordning samma vakuitet tyst.
    expect(controls.some((el) => el.hasAttribute("download"))).toBe(true);

    for (const el of controls) {
      await user.click(el as HTMLElement);
    }

    expect(open).not.toHaveBeenCalled();
  });

  it("triggerAriaLabel blir knappens tillgängliga namn (SC 2.5.3)", () => {
    // #1373: /cv renderar ett kort per CV, så utan detta läser varje trigger identiskt i
    // en skärmläsares knapp-rotor. Kortets egen svit exercerade det här tidigare genom
    // den RIKTIGA komponenten, men den mockar nu CvPreview för att assertera inkopplingen
    // — så kontraktet behöver ett hem här, på den riktiga komponenten. Annars passerar
    // hela sviten när `aria-label` tas bort (test-writer + code-reviewer, PR #1684).
    render(
      <CvPreview
        originalUrl={ORIGINAL_URL}
        triggerAriaLabel="Ladda ner CV-filen: Anna Andersson CV"
      />
    );

    const trigger = screen.getByRole("button", {
      name: "Ladda ner CV-filen: Anna Andersson CV",
    });
    // SC 2.5.3 Label in Name: det tillgängliga namnet måste INNEHÅLLA den synliga texten.
    expect(trigger).toHaveTextContent("Ladda ner CV-filen");
  });

  it("nedladdningen döps efter ytans namn, med ändelsen ur content-type", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} fileName="Anna Andersson CV" />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    const download = await screen.findByRole("link", { name: "Ladda ner" });
    // Utan detta heter varje hämtad fil `original.pdf` och två CV blir omöjliga att
    // skilja åt i nedladdningsmappen.
    expect(download).toHaveAttribute("download", "Anna Andersson CV.pdf");
  });

  it("sökvägstecken i ytans namn når aldrig filnamnet", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    render(
      <CvPreview originalUrl={ORIGINAL_URL} fileName="..\\..\\etc/passwd" />
    );
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    const download = await screen.findByRole("link", { name: "Ladda ner" });
    const name = download.getAttribute("download") ?? "";
    // Värdet blir ett filnamn på användarens disk — en separator där kunde peka utanför
    // nedladdningsmappen. Fixturen bär BÅDA separatorerna: en tidigare version av det här
    // testet asserterade på backslash mot en fixtur UTAN backslash, vilket var sant
    // oavsett vad koden gjorde.
    expect(name).not.toContain("/");
    expect(name).not.toContain("\\");
    expect(name.endsWith(".pdf")).toBe(true);
  });

  it("felgrenen har en åtgärd: Försök igen hämtar om", async () => {
    const user = userEvent.setup();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, status: 500 } as Response)
      .mockResolvedValue(fileResponse(PDF_CONTENT_TYPE));
    global.fetch = fetchMock as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    // Felet avbryter (role="alert"), till skillnad från de artiga utfallen.
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Originalfilen kunde inte laddas.");

    await user.click(screen.getByRole("button", { name: "Försök igen" }));

    expect(
      await screen.findByRole("link", { name: "Ladda ner" })
    ).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("utfallen ligger i en live-region som finns FÖRE sitt innehåll (SC 4.1.3)", async () => {
    const user = userEvent.setup();
    const pending = deferred<Response>();
    global.fetch = vi.fn(() => pending.promise) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    // Regionen är monterad REDAN under laddningen, tom. En artig region som skapas i
    // samma render som sin text annonseras inte av flera skärmläsare — och spinnerns
    // egen role=status avmonteras i exakt den render där utfallet dyker upp.
    // BrandSpinner renderar sin EGEN role=status inuti `.jp-modal-loading`, tidigare i
    // DOM — och det är precis den som inte kan bära utfallet, eftersom den avmonteras i
    // samma render som utfallet dyker upp. Regionen har därför ett eget tillgängligt
    // namn: `getByRole` KASTAR vid tvetydighet, där en `querySelector` tyst hade tagit
    // spinnerns nod i stället.
    const region = screen.getByRole("status", { name: "Filens status" });
    expect(region.textContent).toBe("");

    pending.resolve(fileResponse(PDF_CONTENT_TYPE));
    await screen.findByRole("link", { name: "Ladda ner" });

    // Nodidentitet, inte bara innehåll: det är att SAMMA nod bytte innehåll som en
    // skärmläsare hör. En ny nod med rätt text hade inte annonserats.
    const after = screen.getByRole("status", { name: "Filens status" });
    expect(after).toBe(region);
    expect(after.textContent).toContain("Den visas inte i webbläsaren");
  });

  it("docx: samma nedladdningsform, ändelsen kommer från content-type", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(DOCX_CONTENT_TYPE)) as unknown as typeof fetch;

    const { container } = render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    const download = await screen.findByRole("link", { name: "Ladda ner" });
    // Ändelsen följer svarets content-type, aldrig filnamnet.
    expect(download).toHaveAttribute("download", "original.docx");
    expect(container.querySelector("iframe")).toBeNull();
  });

  // DEKLARERAT OÅTKOMLIGT TILLSTÅND (§5 `Tests:`). Adaptern `original-file-proxy.ts`
  // svarar 502 på varje content-type utanför sin tvåpostslista, så produktionen kan
  // ALDRIG leverera 200 + `text/html` hit; båda call sites går via just de två
  // BFF-routerna. Testet påstår därför ingenting om vad produktionen producerar — bara
  // att LÄSSIDAN degraderar säkert om allowlisten någon gång kringgicks.
  it("okänd content-type (oåtkomlig via BFF:en) → fel-copy, aldrig otypat innehåll", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue(fileResponse("text/html")) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    expect(
      await screen.findByText("Originalfilen kunde inte laddas.")
    ).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Ladda ner" })).not.toBeInTheDocument();
  });

  it("404 → ärligt tomt tillstånd, inte ett fel", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue({ ok: false, status: 404 } as Response) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    // Ett CV skapat i tjänsten, eller en import där filen aldrig sparades, HAR
    // ingen originalfil. Det är ett vanligt utfall och sägs som ett sådant.
    expect(
      await screen.findByText(
        "Det här CV:t har ingen sparad originalfil. En fil finns bara för CV du importerat, och bara om den fick sparas vid importen."
      )
    ).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Ladda ner" })).not.toBeInTheDocument();
  });

  it("429 → civic copy med '30 sekunder', ingen nedladdningslänk", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 429,
      json: async () => ({ error: "rateLimited", retryAfterSeconds: 30 }),
    } as unknown as Response) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    expect(await screen.findByText(/30 sekunder/)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Ladda ner" })).not.toBeInTheDocument();
  });

  it("övrigt fel (500) → civic copy 'kunde inte laddas'", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue({ ok: false, status: 500 } as Response) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

    expect(
      await screen.findByText("Originalfilen kunde inte laddas.")
    ).toBeInTheDocument();
  });

  it("Stäng-knappen stänger modalen, returnerar fokus till triggern och revokar blob-URL:en", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    const trigger = screen.getByRole("button", { name: "Ladda ner CV-filen" });
    await user.click(trigger);
    await screen.findByRole("link", { name: "Ladda ner" });

    await user.click(screen.getByRole("button", { name: "Stäng" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:mock");
  });

  it("Esc stänger modalen", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

    render(<CvPreview originalUrl={ORIGINAL_URL} />);
    await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));
    await screen.findByRole("link", { name: "Ladda ner" });

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  describe("vyval", () => {
    it("visar INGEN flikrad när atsTextUrl saknas (parsat CV)", async () => {
      const user = userEvent.setup();
      global.fetch = vi.fn().mockResolvedValue(fileResponse(PDF_CONTENT_TYPE)) as unknown as typeof fetch;

      render(<CvPreview originalUrl={ORIGINAL_URL} />);
      await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));
      await screen.findByRole("link", { name: "Ladda ner" });

      // En ensam flik är en kontroll som inte kontrollerar något.
      expect(screen.queryByRole("group", { name: "Välj: originalfil eller ATS-text" })).not.toBeInTheDocument();
      expect(
        screen.queryByRole("button", { name: "Textversion för ATS" })
      ).not.toBeInTheDocument();
    });

    it("profilflikarna ATS-profil/Visuell profil finns INTE längre", async () => {
      const user = userEvent.setup();
      global.fetch = routedFetch() as unknown as typeof fetch;

      render(<CvPreview originalUrl={RESUME_ORIGINAL_URL} atsTextUrl={ATS_TEXT_URL} />);
      await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));
      await screen.findByRole("link", { name: "Ladda ner" });

      // Originalet har ingen ATS-variant och ingen visuell variant — det är en fil.
      expect(screen.queryByRole("button", { name: "ATS-profil" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Visuell profil" })).not.toBeInTheDocument();
    });

    it("visar flikarna Originalfil + Textversion för ATS när atsTextUrl ges", async () => {
      const user = userEvent.setup();
      global.fetch = routedFetch() as unknown as typeof fetch;

      render(<CvPreview originalUrl={RESUME_ORIGINAL_URL} atsTextUrl={ATS_TEXT_URL} />);
      await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));

      expect(screen.getByRole("group", { name: "Välj: originalfil eller ATS-text" })).toBeInTheDocument();
      const original = screen.getByRole("button", { name: "Originalfil" });
      expect(original).toHaveAttribute("aria-current", "true");
      expect(screen.getByRole("button", { name: "Textversion för ATS" })).toBeInTheDocument();
    });

    it("aktivering hämtar atsTextUrl och renderar texten + banner-copyn", async () => {
      const user = userEvent.setup();
      const fetchMock = routedFetch();
      global.fetch = fetchMock as unknown as typeof fetch;

      render(<CvPreview originalUrl={RESUME_ORIGINAL_URL} atsTextUrl={ATS_TEXT_URL} />);
      await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));
      await screen.findByRole("link", { name: "Ladda ner" });

      await user.click(screen.getByRole("button", { name: "Textversion för ATS" }));

      expect(await screen.findByText(/Så här läser en ATS-parser ditt CV/)).toBeInTheDocument();
      expect(screen.getByText(/Backend-utvecklare/)).toBeInTheDocument();
      // Textvyn ersätter ramen — de två är olika dokument, aldrig två vyer av ett.
      expect(screen.queryByRole("link", { name: "Ladda ner" })).not.toBeInTheDocument();

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
      await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));
      await screen.findByRole("link", { name: "Ladda ner" });

      await user.click(screen.getByRole("button", { name: "Textversion för ATS" }));

      expect(
        await screen.findByText("Textversionen är inte tillgänglig ännu.")
      ).toBeInTheDocument();
    });

    it("byte tillbaka till Originalfil återställer ramen", async () => {
      const user = userEvent.setup();
      global.fetch = routedFetch() as unknown as typeof fetch;

      render(<CvPreview originalUrl={RESUME_ORIGINAL_URL} atsTextUrl={ATS_TEXT_URL} />);
      await user.click(screen.getByRole("button", { name: "Ladda ner CV-filen" }));
      await screen.findByRole("link", { name: "Ladda ner" });

      await user.click(screen.getByRole("button", { name: "Textversion för ATS" }));
      await screen.findByText(/Så här läser en ATS-parser ditt CV/);

      await user.click(screen.getByRole("button", { name: "Originalfil" }));

      expect(await screen.findByRole("link", { name: "Ladda ner" })).toBeInTheDocument();
    });
  });
});
