import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TemplateBuilder } from "./template-builder";
import type { ActionResult } from "@/lib/actions/resumes";
import type {
  CvTemplateOptionsDto,
  TemplateCatalogDto,
} from "@/lib/dto/resumes";

/**
 * Fas 4b PR-8b 8b.3 — mallbyggarens klient-ö. `updateTemplateOptionsAction` mockas
 * (ön kallar den vid "Spara mall"); signaturen speglar den äkta `(resumeId, options)`.
 * `useTranslations("pages.cv.mall")` är ÄKTA via test-shimmens NextIntlClientProvider
 * (svenska katalogen, precis som produktion). fetch (PDF-blob) + URL.createObjectURL
 * stubbas per test (jsdom implementerar dem inte), mönster från `cv-preview.test.tsx`.
 *
 * Kärninvarianter: (1) tre optionsgrupper (TYPSNITT deferrad, ingen font-grupp);
 * (2) ATS-etiketten läser KATALOGENS atsSafe för den valda mallen (P5, ingen
 * FE-härledning); (3) Spara skickar valen inkl. den BEVARADE fontPair; (4) Uppdatera
 * hämtar render/preview med de fyra parametrarna.
 */

const RESUME_ID = "22222222-2222-4222-8222-222222222222";

const initialOptions: CvTemplateOptionsDto = {
  template: "Klar",
  accentColor: "NavyBlue",
  fontPair: "Modern",
  density: "Normal",
  photoEnabled: false,
  photoShape: "Circle",
  effectiveAtsSafe: true,
};

const catalog: TemplateCatalogDto = {
  templates: [
    { name: "Klar", atsSafe: true },
    { name: "Accentlinje", atsSafe: true },
    { name: "MorkPanel", atsSafe: false },
  ],
  accents: [
    { name: "NavyBlue", hex: "#1E3A5F" },
    { name: "ForestGreen", hex: "#15603F" },
    { name: "WineRed", hex: "#7A2E35" },
    { name: "Graphite", hex: "#3A4451" },
  ],
  fontPairs: [{ name: "Modern" }, { name: "Classic" }],
  densities: [{ name: "Airy" }, { name: "Normal" }, { name: "Compact" }],
};

const updateTemplateOptionsMock =
  vi.fn<(...args: [string, unknown]) => Promise<ActionResult>>();

vi.mock("@/lib/actions/resumes", () => ({
  updateTemplateOptionsAction: (resumeId: string, options: unknown) =>
    updateTemplateOptionsMock(resumeId, options),
}));

/**
 * Minimalt PDF-mock-svar (inte en riktig `Response` runt en `Blob`): ön läser bara
 * `ok` + `blob()` på happy-path. En äkta `new Response(new Blob(...))` läses tillbaka
 * via `Blob.stream()`, vars tillgänglighet skiljer lokal Node från CI:s undici. Samma
 * mönster som `cv-preview.test.tsx`. `URL.createObjectURL` är stubbad → innehållet
 * spelar ingen roll.
 */
function pdfResponse(): Response {
  return {
    ok: true,
    status: 200,
    blob: async () => new Blob(["pdf"], { type: "application/pdf" }),
  } as unknown as Response;
}

/**
 * Fetch-router: första paint (`/preview`) lyckas alltid (pdfResponse), medan den
 * efemära hämtningen (`/render/preview`, triggad av "Uppdatera") ger `ephemeral()`.
 * Så testerna når ready-state (kan klicka Uppdatera) och styr sedan efemär-utfallet.
 */
function fetchWithEphemeral(ephemeral: () => Response) {
  return vi
    .fn()
    .mockImplementation((url: string) =>
      String(url).includes("/render/preview") ? ephemeral() : pdfResponse()
    );
}

function renderBuilder() {
  return render(
    <TemplateBuilder
      resumeId={RESUME_ID}
      initialOptions={initialOptions}
      catalog={catalog}
    />
  );
}

describe("<TemplateBuilder /> (Fas 4b PR-8b 8b.3 — mallbyggare)", () => {
  const originalFetch = global.fetch;
  const originalCreate = URL.createObjectURL;
  const originalRevoke = URL.revokeObjectURL;

  beforeEach(() => {
    URL.createObjectURL = vi.fn(
      () => "blob:mock"
    ) as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = vi.fn() as unknown as typeof URL.revokeObjectURL;
    global.fetch = vi.fn().mockImplementation(() => pdfResponse());
    updateTemplateOptionsMock.mockReset();
    updateTemplateOptionsMock.mockResolvedValue({ success: true });
  });

  afterEach(() => {
    global.fetch = originalFetch;
    URL.createObjectURL = originalCreate;
    URL.revokeObjectURL = originalRevoke;
    vi.restoreAllMocks();
  });

  it("renderar de tre optionsgrupperna (MALL/ACCENTFÄRG/TÄTHET) — ingen TYPSNITT-grupp", async () => {
    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    expect(screen.getByRole("radiogroup", { name: "Mall" })).toBeInTheDocument();
    expect(
      screen.getByRole("radiogroup", { name: "Accentfärg" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("radiogroup", { name: "Täthet" })
    ).toBeInTheDocument();

    // Ett representativt val per grupp (svensk etikett resolvad ur katalog-namnet).
    expect(screen.getByRole("radio", { name: "Klar" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Mörk panel" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Marinblå" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Luftig" })).toBeInTheDocument();

    // TYPSNITT är deferrad (Klas 2026-07-12) → ingen font-grupp.
    expect(
      screen.queryByRole("radiogroup", { name: "Typsnitt" })
    ).not.toBeInTheDocument();
  });

  it("första paint laddar den PERSISTERADE Visual-renderingen", async () => {
    const fetchMock = vi.fn().mockImplementation(() => pdfResponse());
    global.fetch = fetchMock;

    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    const [firstUrl] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(firstUrl).toBe(`/api/cv/${RESUME_ID}/preview?profile=Visual`);
  });

  it("ATS-etiketten speglar den valda mallens katalog-flagga (P5, ingen FE-härledning)", async () => {
    const user = userEvent.setup();
    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    // Initialt Klar (atsSafe: true).
    expect(screen.getByText("Klarar ATS-granskning")).toBeInTheDocument();
    expect(screen.queryByText("Utformad för läsning")).not.toBeInTheDocument();

    // Byt till Mörk panel (MorkPanel, atsSafe: false) → etiketten vänder ärligt.
    await user.click(screen.getByRole("radio", { name: "Mörk panel" }));

    expect(screen.getByText("Utformad för läsning")).toBeInTheDocument();
    expect(
      screen.queryByText("Klarar ATS-granskning")
    ).not.toBeInTheDocument();
  });

  it("'Spara mall' anropar action:en med valen inkl. den BEVARADE fontPair", async () => {
    const user = userEvent.setup();
    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    await user.click(screen.getByRole("radio", { name: "Accentlinje" }));
    await user.click(screen.getByRole("radio", { name: "Skogsgrön" }));
    await user.click(screen.getByRole("radio", { name: "Kompakt" }));
    await user.click(screen.getByRole("button", { name: "Spara mall" }));

    await waitFor(() =>
      expect(updateTemplateOptionsMock).toHaveBeenCalledWith(RESUME_ID, {
        template: "Accentlinje",
        accentColor: "ForestGreen",
        // fontPair rörs aldrig av UI:t (TYPSNITT deferrad) → persisterat värde bevaras.
        fontPair: "Modern",
        density: "Compact",
      })
    );

    expect(await screen.findByText("Mallen sparad.")).toBeInTheDocument();
  });

  it("ytar action-felet i en role='alert' när skrivningen misslyckas", async () => {
    const user = userEvent.setup();
    updateTemplateOptionsMock.mockResolvedValue({
      success: false,
      error: "Kunde inte spara mallen.",
    });
    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    await user.click(screen.getByRole("button", { name: "Spara mall" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Kunde inte spara mallen.");
  });

  it("'Uppdatera förhandsvisning' hämtar render/preview med de fyra parametrarna", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockImplementation(() => pdfResponse());
    global.fetch = fetchMock;

    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    // Byt mall så parametern syns och förhandsvisningen blir stale.
    await user.click(screen.getByRole("radio", { name: "Accentlinje" }));
    await user.click(
      screen.getByRole("button", { name: "Uppdatera förhandsvisning" })
    );

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([url]) =>
          String(url).includes("/render/preview")
        )
      ).toBe(true)
    );

    const call = fetchMock.mock.calls.find(([url]) =>
      String(url).includes("/render/preview")
    ) as [string, RequestInit];
    expect(call[0]).toBe(
      `/api/cv/${RESUME_ID}/render/preview?template=Accentlinje&accent=NavyBlue&font=Modern&density=Normal`
    );
  });

  it("429 vid Uppdatera → previewRateLimited-copyn med de PARSADE sekunderna", async () => {
    const user = userEvent.setup();
    global.fetch = fetchWithEphemeral(
      () =>
        new Response(
          JSON.stringify({ error: "rateLimited", retryAfterSeconds: 45 }),
          { status: 429, headers: { "Content-Type": "application/json" } }
        )
    );

    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");
    await user.click(
      screen.getByRole("button", { name: "Uppdatera förhandsvisning" })
    );

    // ICU-pluralet ger "45 sekunder"; role=status så en skärmläsare får det.
    expect(await screen.findByText(/45 sekunder/)).toBeInTheDocument();
    // Den inaktuella iframe:n är rensad vid Uppdatera.
    expect(
      screen.queryByTitle("Förhandsvisning av CV")
    ).not.toBeInTheDocument();
  });

  it("429 utan parsbar body → 60s default-fallback", async () => {
    const user = userEvent.setup();
    global.fetch = fetchWithEphemeral(() => new Response(null, { status: 429 }));

    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");
    await user.click(
      screen.getByRole("button", { name: "Uppdatera förhandsvisning" })
    );

    expect(await screen.findByText(/60 sekunder/)).toBeInTheDocument();
  });

  it("404 vid Uppdatera → previewNotFound-copyn", async () => {
    const user = userEvent.setup();
    global.fetch = fetchWithEphemeral(() => new Response(null, { status: 404 }));

    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");
    await user.click(
      screen.getByRole("button", { name: "Uppdatera förhandsvisning" })
    );

    expect(await screen.findByText(/kunde inte hittas/)).toBeInTheDocument();
  });

  it("500 vid Uppdatera → previewError-copyn i en role='alert'", async () => {
    const user = userEvent.setup();
    global.fetch = fetchWithEphemeral(() => new Response(null, { status: 500 }));

    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");
    await user.click(
      screen.getByRole("button", { name: "Uppdatera förhandsvisning" })
    );

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/kunde inte laddas/);
  });

  it("stale-indikatorn: dold initialt, syns efter val, försvinner efter Uppdatera", async () => {
    const user = userEvent.setup();
    renderBuilder();

    // Dold under initial load (renderedKey===null null-guard + loading-guard).
    expect(
      screen.queryByText(/visar inte dina senaste val/)
    ).not.toBeInTheDocument();

    // Efter första paint (renderedKey===initialKey, val === persisterat) fortsatt dold.
    await screen.findByTitle("Förhandsvisning av CV");
    expect(
      screen.queryByText(/visar inte dina senaste val/)
    ).not.toBeInTheDocument();

    // Byt mall → förhandsvisningen är nu stale.
    await user.click(screen.getByRole("radio", { name: "Accentlinje" }));
    expect(
      screen.getByText(/visar inte dina senaste val/)
    ).toBeInTheDocument();

    // Uppdatera → efemär render lyckas → renderedKey===currentKey → stale rensas.
    await user.click(
      screen.getByRole("button", { name: "Uppdatera förhandsvisning" })
    );
    await screen.findByTitle("Förhandsvisning av CV");
    await waitFor(() =>
      expect(
        screen.queryByText(/visar inte dina senaste val/)
      ).not.toBeInTheDocument()
    );
  });

  it("okänt katalog-namn utan i18n-nyckel → råvärdet renderas (t.has civic fallback, ingen krasch)", async () => {
    const catalogWithUnknown: TemplateCatalogDto = {
      ...catalog,
      templates: [...catalog.templates, { name: "FramtidaMall", atsSafe: true }],
    };

    render(
      <TemplateBuilder
        resumeId={RESUME_ID}
        initialOptions={initialOptions}
        catalog={catalogWithUnknown}
      />
    );
    await screen.findByTitle("Förhandsvisning av CV");

    // "FramtidaMall" saknar i18n-etikett → t.has faller till råvärdet (ingen
    // MISSING_MESSAGE-krasch, paritet ResumeCard).
    expect(
      screen.getByRole("radio", { name: "FramtidaMall" })
    ).toBeInTheDocument();
  });

  // #820: ATS-utfallet bärs numera av en framträdande StatusPill i stället för en
  // StatusDot. KLASS-namnen ändras, men KONTRAKTET är ruling B — icke-ATS-säker är
  // NEUTRAL, aldrig warning (en mall optimerad för mänskliga läsare är ett giltigt
  // val; amber skulle uppfinna en fara). Det är den assertionen som är testets syfte.
  it("ATS-utfallets pill-ton: success för ATS-säker, neutral (INTE warning) för icke-ATS-säker (ruling B)", async () => {
    const user = userEvent.setup();
    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    // Klar (atsSafe: true) → success-ton + text-flip.
    const safePill = screen.getByText("Klarar ATS-granskning");
    expect(safePill).toHaveClass("jp-pill--success");

    // Mörk panel (MorkPanel, atsSafe: false) → NEUTRAL (ärligt neutral, aldrig warning).
    await user.click(screen.getByRole("radio", { name: "Mörk panel" }));
    const neutralPill = screen.getByText("Utformad för läsning");
    expect(neutralPill).toHaveClass("jp-pill--neutral");
    expect(neutralPill).not.toHaveClass("jp-pill--warning");
  });

  // ---------------------------------------------------------------------------
  // #820 — UI-upplyftets egna kontrakt (kort, swatchar, segment, schematik).
  // ---------------------------------------------------------------------------

  it("mallkorten är riktiga radioknappar med aria-checked (inte klickbara divar)", async () => {
    const user = userEvent.setup();
    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    const klar = screen.getByRole("radio", { name: "Klar" });
    const mork = screen.getByRole("radio", { name: "Mörk panel" });
    expect(klar).toHaveAttribute("aria-checked", "true");
    expect(mork).toHaveAttribute("aria-checked", "false");

    await user.click(mork);
    expect(screen.getByRole("radio", { name: "Mörk panel" })).toHaveAttribute(
      "aria-checked",
      "true"
    );
    expect(screen.getByRole("radio", { name: "Klar" })).toHaveAttribute(
      "aria-checked",
      "false"
    );
  });

  it("kortets beskrivning ligger i aria-describedby, inte i det tillgängliga namnet", async () => {
    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    // Namnet får INTE svälla med beskrivningen (annars blir radion oadresserbar).
    const klar = screen.getByRole("radio", { name: "Klar" });
    const descId = klar.getAttribute("aria-describedby");
    expect(descId).toBeTruthy();

    const desc = document.getElementById(descId as string);
    expect(desc).toHaveTextContent(
      "En spalt. Namn med tunn accentlinje och versala, understrukna rubriker."
    );
  });

  it("schematiken bär den VALDA accentens katalog-hex via CSS-variabeln (aldrig FE-härledd)", async () => {
    const user = userEvent.setup();

    // SENTINEL-hexar, INTE palettens riktiga. Poängen med testet är PROVENIENS: en dev
    // som hårdkodar en FE-färgkarta (`const HEX = { NavyBlue: "#1E3A5F", ... }`) — exakt
    // den P5-överträdelse testet finns för att stoppa — skulle passera grönt mot en
    // fixtur som råkar bära de äkta värdena. Med påhittade hexar kan bara katalogen
    // producera dem.
    const sentinelCatalog: TemplateCatalogDto = {
      ...catalog,
      accents: [
        { name: "NavyBlue", hex: "#AB12CD" },
        { name: "ForestGreen", hex: "#12CD34" },
        { name: "WineRed", hex: "#CD3412" },
        { name: "Graphite", hex: "#345678" },
      ],
    };

    render(
      <TemplateBuilder
        resumeId={RESUME_ID}
        initialOptions={initialOptions}
        catalog={sentinelCatalog}
      />
    );
    await screen.findByTitle("Förhandsvisning av CV");

    // Kortgruppen (radiogroup "Mall") bär datakanalen som schematikens fills läser.
    const grid = screen.getByRole("radiogroup", { name: "Mall" });
    expect(grid.getAttribute("style")).toContain("--jp-mallcard-accent: #AB12CD");

    // Swatch-pricken målas ur SAMMA källa → de två kan aldrig visa olika färg.
    // (jsdom normaliserar background-color till rgb(); #AB12CD = rgb(171, 18, 205).)
    const dot = screen
      .getByRole("radio", { name: "Marinblå" })
      .querySelector(".jp-swatch__dot");
    expect(dot?.getAttribute("style")).toContain("rgb(171, 18, 205)");

    // Byt accent → variabeln följer katalogens hex, inte en FE-lista.
    await user.click(screen.getByRole("radio", { name: "Skogsgrön" }));
    expect(
      screen.getByRole("radiogroup", { name: "Mall" }).getAttribute("style")
    ).toContain("--jp-mallcard-accent: #12CD34");
  });

  it("INGEN auto-render vid valändring — bara det explicita Uppdatera-klicket kostar en render", async () => {
    const user = userEvent.setup();
    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    // Efter första paint (den PERSISTERADE renderingen) ska exakt ett anrop ha skett.
    const fetchMock = global.fetch as ReturnType<typeof vi.fn>;
    expect(fetchMock.mock.calls).toHaveLength(1);
    expect(String(fetchMock.mock.calls[0]?.[0])).toContain("/preview?profile=Visual");

    // Varje efemär render kostar en DEK-dekryptering + en QuestPDF-render bakom en
    // rate limit. Att byta mall/accent/täthet får INTE trigga någon av dem.
    await user.click(screen.getByRole("radio", { name: "Mörk panel" }));
    await user.click(screen.getByRole("radio", { name: "Vinröd" }));
    await user.click(screen.getByRole("radio", { name: "Kompakt" }));

    expect(fetchMock.mock.calls).toHaveLength(1);
    expect(
      fetchMock.mock.calls.some(([url]) => String(url).includes("/render/preview"))
    ).toBe(false);

    // Först knappen kostar något.
    await user.click(
      screen.getByRole("button", { name: "Uppdatera förhandsvisning" })
    );
    await waitFor(() => expect(fetchMock.mock.calls).toHaveLength(2));
    expect(String(fetchMock.mock.calls[1]?.[0])).toContain("/render/preview");
  });

  it("object-URL:er revokeras: den gamla släpps vid ny render, och den sista vid unmount", async () => {
    const user = userEvent.setup();
    const { unmount } = renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    const revoke = URL.revokeObjectURL as ReturnType<typeof vi.fn>;
    expect(revoke).not.toHaveBeenCalled();

    // En ny render ersätter den gamla blobben → den gamla MÅSTE släppas.
    await user.click(
      screen.getByRole("button", { name: "Uppdatera förhandsvisning" })
    );
    await waitFor(() => expect(revoke).toHaveBeenCalled());

    // Och den sista släpps när ön lämnar DOM:en. Utan detta läcker en sida vars hela
    // syfte är upprepade PDF-renders en blob per klick.
    revoke.mockClear();
    unmount();
    expect(revoke).toHaveBeenCalled();
  });

  it("snabb mount→unmount hinner inte spendera en render (första paint är macrotask-schemalagd)", () => {
    const fetchMock = global.fetch as ReturnType<typeof vi.fn>;
    const { unmount } = renderBuilder();

    // Effekten schemalägger hämtningen på setTimeout(0) och rensar den i sin cleanup.
    // Lämnar användaren sidan direkt kostar besöket NOLL renders.
    unmount();
    expect(fetchMock.mock.calls).toHaveLength(0);
  });

  it.each([
    ["Mall", "Mörk panel"],
    ["Accentfärg", "Skogsgrön"],
    ["Täthet", "Kompakt"],
  ])(
    "spara-kvittot försvinner när %s ändras — det gäller inte längre det nya valet",
    async (_group, option) => {
      const user = userEvent.setup();
      renderBuilder();
      await screen.findByTitle("Förhandsvisning av CV");

      await user.click(screen.getByRole("button", { name: "Spara mall" }));
      expect(await screen.findByText("Mallen sparad.")).toBeInTheDocument();

      // Ett kvitto som ljuger är värre än inget kvitto.
      await user.click(screen.getByRole("radio", { name: option }));
      expect(screen.queryByText("Mallen sparad.")).not.toBeInTheDocument();
    }
  );

  it("ett valbyte UNDER en pågående Spara får inte kvitteras när skrivningen landar", async () => {
    const user = userEvent.setup();

    // Håll Server Action:en i luften tills vi bestämmer oss.
    let resolveSave: (value: { success: true }) => void = () => {};
    updateTemplateOptionsMock.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveSave = resolve;
        })
    );

    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    await user.click(screen.getByRole("button", { name: "Spara mall" }));
    // Användaren hinner byta mall medan skrivningen är i luften.
    await user.click(screen.getByRole("radio", { name: "Mörk panel" }));

    resolveSave({ success: true });

    // Kvittot gällde det GAMLA valet. Skulle det dyka upp nu stod "Mallen sparad."
    // bredvid en mall som aldrig sparades.
    await waitFor(() =>
      expect(screen.queryByText("Mallen sparad.")).not.toBeInTheDocument()
    );
  });

  it("täthet renderas som segmented control med radio-semantik", async () => {
    const user = userEvent.setup();
    renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    const kompakt = screen.getByRole("radio", { name: "Kompakt" });
    expect(kompakt).toHaveAttribute("aria-checked", "false");

    await user.click(kompakt);
    expect(screen.getByRole("radio", { name: "Kompakt" })).toHaveAttribute(
      "aria-checked",
      "true"
    );
  });

  it("exakt en primärknapp på sidan (ADR 0038) och det är Spara mall", async () => {
    const { container } = renderBuilder();
    await screen.findByTitle("Förhandsvisning av CV");

    const primaries = container.querySelectorAll(".jp-btn--primary");
    expect(primaries).toHaveLength(1);
    expect(primaries[0]).toHaveTextContent("Spara mall");
  });

  it("okänd katalogmall får varken beskrivning eller accentfärgad schematik", async () => {
    const catalogWithUnknown: TemplateCatalogDto = {
      ...catalog,
      templates: [...catalog.templates, { name: "FramtidaMall", atsSafe: true }],
    };

    render(
      <TemplateBuilder
        resumeId={RESUME_ID}
        initialOptions={initialOptions}
        catalog={catalogWithUnknown}
      />
    );
    await screen.findByTitle("Förhandsvisning av CV");

    // FramtidaMall saknar både etikett och beskrivning → inget påhittat.
    const framtida = screen.getByRole("radio", { name: "FramtidaMall" });
    expect(framtida).not.toHaveAttribute("aria-describedby");

    // Fail-safe: en oklassificerad mall gör INGET färgpåstående (noll accent-element).
    const svg = framtida.querySelector("svg");
    expect(svg).not.toBeNull();
    expect(svg?.querySelectorAll(".jp-schem__accent")).toHaveLength(0);
  });
});
