import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvCompleteGuide } from "./cv-complete-guide";
import type {
  ParsedContentDto,
  ParseConfidenceDto,
  CvSectionSuggestionsDto,
} from "@/lib/dto/parsed-resume";
import type { ResumeContentDto } from "@/lib/types/resumes";
import type { ActionResult } from "@/lib/actions/resumes";

/**
 * Fas 4b PR-8.3 — Slutför-guiden (fyra steg, bekräfta-inte-fyll). Kärnbeteenden,
 * inte uttömmande. `promoteParsedResumeFromGuideAction` mockas (klient-ön kallar
 * den vid Spara); `useRouter` mockas för Stäng-navigeringen. `useTranslations`
 * (validation + resumes.guide) och schemat (`makePromoteParsedResumeSchema`) är
 * ÄKTA — testet kör mot de svenska katalogerna, precis som produktion.
 */

const promoteMock =
  vi.fn<(...args: [string, string, ResumeContentDto]) => Promise<ActionResult>>();

vi.mock("@/lib/actions/resumes", () => ({
  promoteParsedResumeFromGuideAction: (
    parsedId: string,
    name: string,
    content: ResumeContentDto,
  ) => promoteMock(parsedId, name, content),
}));

const pushMock = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

const PARSED_ID = "11111111-1111-4111-8111-111111111111";

const CONFIDENCE: ParseConfidenceDto = {
  overall: "Confident",
  requiresManualReview: false,
  fallback: "None",
  sections: [],
};

function makeContent(overrides: Partial<ParsedContentDto> = {}): ParsedContentDto {
  return {
    // #815: fria sektioner - tomma om inget test sager annat.
    sections: [],
    // #844/ADR 0109: ingen oklassificerad preambel om inget test sager annat.
    preamble: null,
    contact: { fullName: "Anna Andersson", email: null, phone: null, location: null },
    profile: null,
    experiences: [],
    educations: [],
    skills: [],
    languages: [],
    ...overrides,
  };
}

function renderGuide(
  content: ParsedContentDto,
  sectionSuggestions: CvSectionSuggestionsDto | null = null,
) {
  return render(
    <CvCompleteGuide
      parsedId={PARSED_ID}
      sourceFileName="cv.pdf"
      content={content}
      confidence={CONFIDENCE}
      sectionSuggestions={sectionSuggestions}
    />,
  );
}

/** Vård-nyttolasten servern skickar för en undersköterska (8b.4a). Rubrikerna är
 *  backendens — de skrivs IN i CV:t och backend har redan bevisat att parsning-lexikonet
 *  känner igen dem. */
const VARD_SUGGESTIONS: CvSectionSuggestionsDto = {
  branschgrupp: "vard",
  hasOccupationPreference: true,
  rationale: "Vanligt inom vård och omsorg",
  suggestions: [
    { sectionId: "legitimation", heading: "Legitimation och intyg", isStandard: true },
    { sectionId: "kurser", heading: "Kurser och intyg", isStandard: false },
    { sectionId: "korkort", heading: "Körkort", isStandard: false },
  ],
};

beforeEach(() => {
  promoteMock.mockReset();
  // Faithful mock: den äkta actionen redirectar (kastar NEXT_REDIRECT) vid framgång
  // och returnerar bara { success:false } vid fel. En never-resolving promise
  // speglar success-vägen utan att komponentens `!result.success`-gren nås.
  promoteMock.mockImplementation(() => new Promise<ActionResult>(() => {}));
  pushMock.mockReset();
});

describe("CvCompleteGuide — steg 1 (bekräfta-inte-fyll)", () => {
  it("visar bekräfta-rad för hittat fält (check + värde + Ändra) och input med 'Saknades i filen' för det som saknas", () => {
    renderGuide(
      makeContent({
        contact: {
          fullName: "Anna Andersson",
          email: null,
          phone: "070-000 00 00",
          location: "Göteborg",
        },
        profile: "Erfaren backend-utvecklare",
      }),
    );

    // Hittat fält (fullName) i bekräfta-läge: värdet visas + en "Ändra"-kontroll.
    expect(screen.getByText("Anna Andersson")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ändra Fullständigt namn" }),
    ).toBeInTheDocument();
    // Profilen hittades → "Hittad i filen".
    expect(screen.getByText("Hittad i filen")).toBeInTheDocument();

    // Saknat fält (email) renderas som input med "Saknades i filen"-markering.
    expect(screen.getByLabelText("E-post")).toBeInTheDocument();
    expect(screen.getByText("Saknades i filen")).toBeInTheDocument();
  });

  it("ordräknaren visar '{n} ord' och INGEN 'inom gränsen'-text", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent());

    // Tom profil → "0 ord".
    expect(screen.getByText("0 ord")).toBeInTheDocument();

    await user.type(screen.getByLabelText("Sammanfattning"), "en två tre");

    expect(screen.getByText("3 ord")).toBeInTheDocument();
    // Guiden räknar ord — den påstår aldrig en gräns.
    expect(screen.queryByText(/inom gränsen/i)).not.toBeInTheDocument();
  });
});

describe("CvCompleteGuide — steg-navigering", () => {
  it("klick på steg 3 i skenan flyttar fokus till Kompetenser-rubriken", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent());

    await user.click(screen.getByRole("button", { name: /Kompetenser/ }));

    // Steg-rubriken är h2 (skill-sektionens egen rubrik är h3 med samma text) —
    // scopa på level så matchningen blir entydig.
    const heading = screen.getByRole("heading", { name: "Kompetenser", level: 2 });
    expect(heading).toBeInTheDocument();
    await waitFor(() => expect(heading).toHaveFocus());
  });
});

describe("CvCompleteGuide — steg 3 (kompetens-chips)", () => {
  it("lägger till en kompetens via footer-input + Enter och tar bort via chip-×", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent());

    await user.click(screen.getByRole("button", { name: /Kompetenser/ }));

    // Tomt utgångsläge.
    expect(screen.getByText("Inga kompetenser tillagda.")).toBeInTheDocument();

    // Lägg till via Enter i add-fältet (sr-only label "Lägg till kompetens").
    await user.type(screen.getByLabelText("Lägg till kompetens"), "React{Enter}");

    expect(screen.getByText("React")).toBeInTheDocument();
    expect(
      screen.queryByText("Inga kompetenser tillagda."),
    ).not.toBeInTheDocument();

    // Ta bort via chip-× (aria-label "Ta bort React").
    await user.click(screen.getByRole("button", { name: "Ta bort React" }));

    expect(screen.queryByText("React")).not.toBeInTheDocument();
    expect(screen.getByText("Inga kompetenser tillagda.")).toBeInTheDocument();
  });
});

describe("CvCompleteGuide — steg 2 (Pågående-toggle)", () => {
  it("Pågående döljer slutdatum-fältet", async () => {
    const user = userEvent.setup();
    renderGuide(
      makeContent({
        experiences: [
          {
            title: "Utvecklare",
            organization: "Acme AB",
            period: "2020–2022",
            rawText: "Byggde saker.",
          },
        ],
      }),
    );

    await user.click(
      screen.getByRole("button", { name: /Erfarenhet och utbildning/ }),
    );

    // Parsad post seedas kollapsad → expandera via "Ändra".
    await user.click(screen.getByRole("button", { name: "Ändra erfarenhet 1" }));

    expect(screen.getByLabelText("Slutdatum (valfritt)")).toBeInTheDocument();

    await user.click(screen.getByRole("switch", { name: "Pågående" }));

    expect(screen.queryByLabelText("Slutdatum (valfritt)")).not.toBeInTheDocument();
  });
});

describe("CvCompleteGuide — Stäng-bekräftelse (honesty pin)", () => {
  it("renderar aldrig 'spara utkast' någonstans i utdatan (honesty bind 2)", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent());

    // Även efter en ändring lovar guiden aldrig ett "spara utkast".
    await user.type(screen.getByLabelText("Sammanfattning"), "Ny text");

    expect(document.body.textContent ?? "").not.toMatch(/spara utkast/i);
  });

  // Regression-pin (test-writer-fynd PR-8.3, fixat in-block): RHF:s formState är
  // en Proxy — isDirty måste läsas UNDER RENDER för att prenumereras. Läses den
  // bara i requestClose-handlern förblir den false och Stäng med ändrad form
  // navigerar bort utan bekräftelse (tyst dataförlust, tvärtemot honesty-copyn).
  it(
    "Stäng med ändrad form öppnar bekräfta-dialogen med exakt copy",
    async () => {
      const user = userEvent.setup();
      renderGuide(makeContent());

      await user.type(screen.getByLabelText("Sammanfattning"), "Ny text");
      await user.click(screen.getByRole("button", { name: "Stäng" }));

      const dialog = await screen.findByRole("dialog", {}, { timeout: 400 });
      expect(within(dialog).getByText("Stäng guiden?")).toBeInTheDocument();
      expect(
        within(dialog).getByText(
          "Ändringarna du gjort här sparas inte. Ditt inlästa CV finns kvar på CV-sidan.",
        ),
      ).toBeInTheDocument();
    },
  );
});

describe("CvCompleteGuide — Spara (befordran)", () => {
  it("mappar submit till promoteParsedResumeFromGuideAction med languages (NotStated) och sections i payloaden", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent({ languages: ["Svenska"] }));

    // Lägg till en egen sektion på steg 2 så payloaden bär en sektion.
    await user.click(
      screen.getByRole("button", { name: /Erfarenhet och utbildning/ }),
    );
    await user.click(screen.getByRole("button", { name: /Lägg till sektion/ }));

    const heading = document.querySelector<HTMLInputElement>(
      "#guide-section-0-heading",
    );
    const entryTitle = document.querySelector<HTMLInputElement>(
      "#guide-section-0-entry-0-title",
    );
    expect(heading).not.toBeNull();
    expect(entryTitle).not.toBeNull();
    await user.type(heading!, "Projekt");
    await user.type(entryTitle!, "Jobbpilot");

    // Till Spara-steget och submit (namnet är förifyllt från fullName).
    await user.click(screen.getByRole("button", { name: "Spara" }));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    await waitFor(() => expect(promoteMock).toHaveBeenCalledTimes(1));
    const [parsedId, name, content] = promoteMock.mock.calls[0]!;
    expect(parsedId).toBe(PARSED_ID);
    expect(name).toBe("Anna Andersson");
    // Språk bär NotStated (aldrig syntetiserad nivå).
    expect(content.languages).toEqual([
      { name: "Svenska", proficiency: "NotStated" },
    ]);
    // Egna sektioner flödar genom i payloaden.
    expect(content.sections).toEqual([
      { heading: "Projekt", entries: [{ title: "Jobbpilot", lines: [] }] },
    ]);
  });
});

describe("CvCompleteGuide — datumflaggan säger sanning (#815)", () => {
  // Klas live-review: en post som tydligt läser "2005 - nu" i CV:t flaggades ändå
  // "Datum saknas". Parsern gissar ALDRIG datum (DQ3-3a), så det strukturerade
  // startdatumet är alltid tomt från början — och guiden renderar samtidigt den
  // tolkade perioden precis ovanför flaggan. Pillen påstod alltså att datumen saknades
  // en rad under datumen. Det som saknas är BEKRÄFTELSEN, inte datumen.

  it("periodHint finns → 'Bekräfta datum', inte 'Datum saknas'", async () => {
    const user = userEvent.setup();
    renderGuide(
      makeContent({
        experiences: [
          {
            title: "Operatör",
            organization: "Verkstaden AB",
            period: "2005 - nu",
            rawText: "Operatör, Verkstaden AB",
          },
        ],
      })
    );

    await user.click(screen.getByRole("button", { name: /erfarenhet/i }));

    expect(screen.queryByText("Datum saknas")).not.toBeInTheDocument();
    expect(screen.getByText("Bekräfta datum")).toBeInTheDocument();
  });

  it("ingen periodHint → 'Datum saknas' (då är påståendet sant)", async () => {
    const user = userEvent.setup();
    renderGuide(
      makeContent({
        experiences: [
          {
            title: "Operatör",
            organization: "Verkstaden AB",
            period: null,
            rawText: "Operatör, Verkstaden AB",
          },
        ],
      })
    );

    await user.click(screen.getByRole("button", { name: /erfarenhet/i }));

    expect(screen.getByText("Datum saknas")).toBeInTheDocument();
    expect(screen.queryByText("Bekräfta datum")).not.toBeInTheDocument();
  });
});

describe("CvCompleteGuide — en TITELLÖS prefylld post går att spara (#815)", () => {
  // Det här är testet som saknades, och som lät regressionen slinka igenom hela vägen till
  // granskning: det befintliga spar-testet SKRIVER IN titeln för hand, så det körde aldrig
  // kedjan prefyll → Spara. Parsern sätter medvetet title = null för en enradig eller
  // punktlistad post (den hittar aldrig på en rubrik, ADR 0071) — och skrivytan krävde en
  // titel. Följden var att användaren såg sitt eget innehåll prefyllt i guiden och sedan
  // nekades att spara det, med enda utvägen att radera exakt det innehållet.
  it("Referenser / Lämnas på begäran. (ingen titel) blockerar inte Spara", async () => {
    const user = userEvent.setup();
    promoteMock.mockClear();

    renderGuide(
      makeContent({
        sections: [
          {
            heading: "Referenser",
            entries: [{ title: null, lines: ["Lämnas på begäran."] }],
          },
        ],
      }),
    );

    // Till Spara-steget och submit — INGEN titel skrivs in för hand. Det är hela poängen.
    await user.click(screen.getByRole("button", { name: "Spara" }));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    // Före fixen stannade valideringen här: "Titel krävs på sektionspost."
    await waitFor(() => expect(promoteMock).toHaveBeenCalledTimes(1));

    const [, , content] = promoteMock.mock.calls[0]! as [
      string,
      string,
      {
        sections: Array<{
          heading: string;
          entries: Array<{ title?: string | null; lines?: string[] }>;
        }>;
      },
    ];

    // Innehållet överlever — och titeln förblir tom, aldrig påhittad.
    expect(content.sections[0]!.heading).toBe("Referenser");
    expect(content.sections[0]!.entries[0]!.lines).toContain("Lämnas på begäran.");
    expect(content.sections[0]!.entries[0]!.title ?? "").toBe("");
  });
});

describe("CvCompleteGuide — fria sektioner prefylls (#815)", () => {
  it("PROJEKT från CV:t hamnar i sektionsfältet, med rubriken ordagrant", async () => {
    const user = userEvent.setup();
    renderGuide(
      makeContent({
        sections: [
          {
            heading: "PROJEKT",
            entries: [
              { title: "Betalplattform", lines: ["Byggde en betaltjänst i .NET."] },
            ],
          },
        ],
      })
    );

    // Navigera till sista steget (sektioner).
    await user.click(screen.getByRole("button", { name: /erfarenhet/i }));

    // Rubriken är användarens egen — "PROJEKT", inte "projekt".
    expect(await screen.findByDisplayValue("PROJEKT")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Betalplattform")).toBeInTheDocument();
  });

  // #815 fynd 6: sektionspanelen bar ingen proveniens alls, medan Erfarenhet och
  // Utbildning bar sin. En prefylld PROJEKT-sektion såg därför ut som något guiden
  // hittat på i stället för något som KOM ur filen.
  it("prefylld sektion ytar proveniensen ('1 hittad')", async () => {
    const user = userEvent.setup();
    renderGuide(
      makeContent({
        sections: [
          { heading: "PROJEKT", entries: [{ title: "Betalplattform", lines: [] }] },
        ],
      }),
    );

    await user.click(screen.getByRole("button", { name: /erfarenhet/i }));

    const sectionsHeading = screen.getByRole("heading", { name: "Egna sektioner" });
    const panel = sectionsHeading.closest(".jp-guide__section");
    expect(panel).not.toBeNull();
    expect(within(panel as HTMLElement).getByText("1 hittad")).toBeInTheDocument();
  });

  // Motsatsen, och den fällan proveniensen INTE får gå i: fria sektioner är valfria.
  // Ett CV utan PROJEKT saknar ingenting — "Saknades i filen" hade påstått en lucka
  // som inte finns (till skillnad från Erfarenhet, som ÄR en uppgift att stänga).
  it("utan sektioner i filen påstås INGEN lucka ('Saknades i filen' uteblir i panelen)", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent());

    await user.click(screen.getByRole("button", { name: /erfarenhet/i }));

    const sectionsHeading = screen.getByRole("heading", { name: "Egna sektioner" });
    const panel = sectionsHeading.closest(".jp-guide__section");
    expect(panel).not.toBeNull();
    expect(
      within(panel as HTMLElement).queryByText("Saknades i filen"),
    ).not.toBeInTheDocument();
    // Tomt läge bärs av panelens egen text, inte av ett falskt lucke-påstående.
    expect(
      within(panel as HTMLElement).getByText("Inga egna sektioner tillagda."),
    ).toBeInTheDocument();
  });
});

describe("CvCompleteGuide — stegstatus säger sanning (#815 fynd 6, CTO-bind Q3-A)", () => {
  // Den gamla indikatorn var NÄRVARO-baserad: `experiences.length > 0`. En appendad
  // men tom post räknades därmed som "ifylld" → steget blev grönbockat medan submit
  // ändå nekade. Bocken påstår nu exakt en sak: ifyllt OCH inget som hindrar sparande.
  // Railens knappar bär sin status i det tillgängliga namnet (sr-only), och på steg 2
  // finns dessutom "Lägg till erfarenhet"/"Ta bort erfarenhet 1". Scopa därför alltid
  // till <nav> — annars matchar en fritextregex både railen och panelens knappar.
  function railStep(name: RegExp) {
    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    return within(rail).getByRole("button", { name });
  }

  it("tom appendad erfarenhet grönbockar INTE steget — den flaggas som fel att rätta", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent());

    await user.click(railStep(/erfarenhet/i));
    await user.click(screen.getByRole("button", { name: /Lägg till erfarenhet/ }));

    // Posten finns men är tom → företag/roll/startdatum saknas → steget BLOCKERAR.
    // Före fixen räknades den som närvarande ("length > 0") → grön bock, medan
    // submit ändå nekade.
    await waitFor(() =>
      expect(
        within(railStep(/erfarenhet/i)).getByText(/att åtgärda/),
      ).toBeInTheDocument(),
    );
    expect(
      within(railStep(/erfarenhet/i)).queryByText("klart"),
    ).not.toBeInTheDocument();
  });

  it("orört steg är 'kvar', inte 'klart' — schemat tillåter tomma listor, men inget är ifyllt", () => {
    renderGuide(makeContent());

    // Steg 2 är orört: noll valideringsfel (tomma arrayer ÄR giltiga enligt schemat)
    // men två uppgifter kvar. En ren validitets-backning hade grönbockat det här.
    const step = railStep(/erfarenhet/i);
    expect(within(step).getByText("2 uppgifter kvar")).toBeInTheDocument();
    expect(within(step).queryByText("klart")).not.toBeInTheDocument();
  });

  it("ifyllt och giltigt steg är 'klart'", () => {
    renderGuide(
      makeContent({
        contact: {
          fullName: "Anna Andersson",
          email: "anna@exempel.se",
          phone: "070-000 00 00",
          location: "Göteborg",
        },
        profile: "Erfaren backend-utvecklare.",
      }),
    );

    // Steg 1: alla fem uppgifter ifyllda ur filen, inga valideringsfel → bocken
    // är sann i båda leden (ifyllt OCH inget som hindrar sparande).
    // ^Uppgifter: andra steg bär "N uppgifter kvar" i sitt sr-only-namn.
    const step = railStep(/^Uppgifter/);
    expect(within(step).getByText("klart")).toBeInTheDocument();
    expect(within(step).queryByText(/uppgifter kvar/)).not.toBeInTheDocument();
  });
});

describe("CvCompleteGuide — form-semantik och per-fält-fel (#815 fynd 6)", () => {
  // Roten var en <div> och Spara en type="button" → Enter gjorde ingenting alls,
  // och required/aria-required kunde aldrig utlösa någon validering.
  // NB: Enter i en <textarea> ger radbrytning, inte submit — så beviset måste ske i
  // en <input>. (Det är också därför sammanfattnings-textarean inte "råkar" spara.)
  it("Enter i ett textfält på steg 1 går VIDARE till steg 2 (sparar inte)", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent());

    await user.type(screen.getByLabelText("E-post"), "anna@exempel.se{Enter}");

    // Enter avancerade steget — och nådde absolut inte spar-vägen.
    expect(
      await screen.findByRole("heading", { name: "Erfarenhet och utbildning" }),
    ).toBeInTheDocument();
    expect(promoteMock).not.toHaveBeenCalled();
  });

  // Major (code-reviewer): `clearErrors()` körde på VARJE submit — och "Nästa" ÄR en
  // submit. Ett fel man just fått syn på försvann alltså när man gick vidare för att
  // rätta något annat, medan railen fortsatte visa "1 fel behöver rättas". Fälten och
  // railen sa emot varandra, och foten påstod "De är markerade nedan" om ingenting.
  // Felen härleds nu ur live-parsen (samma källa som railen) och kan inte divergera.
  it("per-fält-felet ÖVERLEVER ett stegbyte (och slocknar när felet är rättat)", async () => {
    const user = userEvent.setup();
    renderGuide(
      makeContent({ contact: { fullName: "", email: null, phone: null, location: null } }),
    );

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });

    // Framkalla det blockerande felet (tomt CV-namn) på Spara-steget.
    await user.click(within(rail).getByRole("button", { name: /^Spara/ }));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));
    await waitFor(() =>
      expect(screen.getByLabelText(/Namn på CV/)).toHaveAttribute(
        "aria-invalid",
        "true",
      ),
    );

    // Gå till ett annat steg och tillbaka — felet ska stå kvar. (Före fixen var det
    // borta, medan railen fortfarande sa att det fanns ett fel att rätta.)
    await user.click(within(rail).getByRole("button", { name: /^Uppgifter/ }));
    await user.click(within(rail).getByRole("button", { name: /^Spara/ }));

    expect(screen.getByLabelText(/Namn på CV/)).toHaveAttribute(
      "aria-invalid",
      "true",
    );

    // Och när felet FAKTISKT är rättat slocknar det — utan ett nytt submit-varv.
    await user.type(screen.getByLabelText(/Namn på CV/), "Mitt CV");
    await waitFor(() =>
      expect(screen.getByLabelText(/Namn på CV/)).not.toHaveAttribute(
        "aria-invalid",
      ),
    );
  });

  // Major (code-reviewer, andra ronden): fotens text var state satt vid submit, medan
  // fältmarkeringarna var live-härledda. Rättade användaren felet slocknade markeringen
  // medan foten stod kvar och sa "De är markerade nedan" — om ingenting. Samma sjukdom
  // som clearErrors-buggen, en yta bort. Foten härleds nu ur samma parse.
  it("fotens 'markerade nedan' FÖRSVINNER när felet faktiskt är rättat", async () => {
    const user = userEvent.setup();
    // Giltigt utgångsläge (namnet seedas från fullName) → ETT fel att isolera:
    // vi tömmer CV-namnet själva. Sätter man fullName tomt får man dessutom ett fel
    // på steg 1, och då står fotens påstående kvar med rätta.
    renderGuide(makeContent());

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Spara/ }));
    await user.clear(screen.getByLabelText(/Namn på CV/));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    // Foten påstår att något är markerat...
    const footError = await screen.findByRole("alert");
    expect(footError.textContent ?? "").toMatch(/markerade nedan/i);

    // ...och slutar påstå det i samma stund påståendet blir falskt.
    await user.type(screen.getByLabelText(/Namn på CV/), "Mitt CV");

    await waitFor(() =>
      expect(screen.queryByRole("alert")).not.toBeInTheDocument(),
    );
  });

  // Före fixen ytades ETT fel i taget (issues[0]) i foten. Fem fel = fem submit-varv.
  it("ett blockerande fel landar PÅ sitt fält, med aria-invalid + aria-describedby", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent({ contact: { fullName: "", email: null, phone: null, location: null } }));

    // CV-namnet seedas från fullName → tomt → blockerande fel på Spara-steget.
    // Railens Spara-knapp bär nu sin status i namnet ("Spara 1 fel behöver rättas"),
    // så matcha på prefix i stället för exakt sträng.
    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Spara/ }));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    const nameInput = await screen.findByLabelText(/Namn på CV/);
    await waitFor(() =>
      expect(nameInput).toHaveAttribute("aria-invalid", "true"),
    );

    // Felet är kopplat till fältet (inte bara en rad i foten) — och beskriver det.
    const describedBy = nameInput.getAttribute("aria-describedby") ?? "";
    expect(describedBy).toContain("guide-cv-name-error");
    const errorEl = document.getElementById("guide-cv-name-error");
    expect(errorEl?.textContent ?? "").not.toBe("");

    // Backend nåddes aldrig.
    expect(promoteMock).not.toHaveBeenCalled();
  });
});

describe("CvCompleteGuide — chip-listan är ingen återvändsgränd (#815, CTO Q3-B)", () => {
  // Parsern cappar ANTALET kompetenser (MaxSkills = 200) men aldrig LÄNGDEN: en lång
  // punkt utan komma blir ett chip på över 100 tecken, vilket schemat fäller. Chips
  // gick bara att TA BORT, och felet ytades ingenstans — foten sa "De är markerade
  // nedan" medan ingenting var markerat. Den användaren kunde alltså inte spara sitt
  // CV, och enda utvägen var att radera innehåll parsern lyft ur hennes egen fil.
  const LONG_SKILL =
    "Erfaren backend-utvecklare med djup kunskap inom distribuerade system och molnarkitektur samt lång vana av att leda tekniska initiativ";

  it("ett för långt parsat chip NAMNGES vid sin lista i stället för att försvinna", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent({ skills: [LONG_SKILL] }));

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Spara/ }));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    // Felet ytas — vid listan, och det säger VILKET chip som fälls (i stället för
    // att försvinna helt, vilket gjorde chippet omöjligt att hitta OCH att spara).
    const addInput = document.querySelector<HTMLInputElement>("#guide-skills-add");
    expect(addInput).not.toBeNull();
    // Add-fältet pekar på felet (routeToError flyttar fokus hit → det läses upp).
    // Men det är INTE aria-invalid: fältets eget värde är giltigt — det är listan
    // som fälls, och att märka kontrollen vore ett falskt påstående till AT.
    await waitFor(() =>
      expect(addInput!.getAttribute("aria-describedby")).toBe(
        "guide-skills-add-error",
      ),
    );
    expect(addInput!.getAttribute("aria-invalid")).toBeNull();
    const errorId = addInput!.getAttribute("aria-describedby");
    expect(errorId).toBe("guide-skills-add-error");
    const errorEl = document.getElementById(errorId!);
    expect(errorEl?.textContent ?? "").toContain(LONG_SKILL);

    // Och sparandet nådde aldrig backend.
    expect(promoteMock).not.toHaveBeenCalled();
  });

  it("'Ändra' lyfter chippet till inmatningsfältet så texten kan KORTAS, inte bara raderas", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent({ skills: [LONG_SKILL] }));

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Kompetenser/ }));

    await user.click(screen.getByRole("button", { name: `Ändra ${LONG_SKILL}` }));

    // Chippet ligger nu i inmatningsfältet, redo att kortas — innehållet är inte förlorat.
    const addInput = document.querySelector<HTMLInputElement>("#guide-skills-add");
    expect(addInput).not.toBeNull();
    expect(addInput!.value).toBe(LONG_SKILL);

    // Korta texten och bekräfta → chippet ERSÄTTS (ingen dubblett).
    await user.clear(addInput!);
    await user.type(addInput!, "Backend-utveckling{Enter}");

    expect(screen.getByText("Backend-utveckling")).toBeInTheDocument();
    expect(screen.queryByText(LONG_SKILL)).not.toBeInTheDocument();
  });

  it("Ändra på ett chip och sedan på ett ANNAT förstör inte det första", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent({ skills: ["React", "TypeScript"] }));

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Kompetenser/ }));

    await user.click(screen.getByRole("button", { name: "Ändra React" }));
    await user.click(screen.getByRole("button", { name: "Ändra TypeScript" }));

    // Båda finns kvar. (Tidigare utkast tog bort chippet vid "Ändra" → det första
    // skrevs över i draft-fältet och var borta för gott.)
    expect(screen.getByText("React")).toBeInTheDocument();
    expect(screen.getByText("TypeScript")).toBeInTheDocument();
  });

  // Stale-index-fällan: `editingIndex` är ett INDEX, och listan kan ändras under tiden.
  // Tar man bort ett chip FÖRE det man redigerar glider allt ett steg ned — och ett
  // commit hade då skrivit över grannen, tyst, över användarens eget innehåll.
  it("borttagning av ett chip FÖRE det man redigerar skriver inte över fel chip", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent({ skills: ["Java", "React", "SQL"] }));

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Kompetenser/ }));

    // Redigera "SQL" (index 2), ta sedan bort "Java" (index 0) → SQL blir index 1.
    await user.click(screen.getByRole("button", { name: "Ändra SQL" }));
    await user.click(screen.getByRole("button", { name: "Ta bort Java" }));

    // Bekräfta ändringen: SQL ska bli PostgreSQL — React får inte röras.
    const addInput = document.querySelector<HTMLInputElement>("#guide-skills-add");
    await user.clear(addInput!);
    await user.type(addInput!, "PostgreSQL{Enter}");

    expect(screen.getByText("React")).toBeInTheDocument();
    expect(screen.getByText("PostgreSQL")).toBeInTheDocument();
    expect(screen.queryByText("SQL")).not.toBeInTheDocument();
    expect(screen.queryByText("Java")).not.toBeInTheDocument();
  });

  // Fällan som en tidigare version av "Ändra" införde: den PLOCKADE BORT chippet och la
  // texten i draft-fältet. Draften är komponent-lokal — ett stegbyte avmonterar
  // ChipEditor — så ett avbrutet redigeringsförsök raderade chippet för gott. Det vore
  // att införa exakt den dataförlust affordansen finns till för att ta bort.
  it("avbruten ändring (stegbyte mitt i) förstör INTE chippet", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent({ skills: ["React"] }));

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Kompetenser/ }));
    await user.click(screen.getByRole("button", { name: "Ändra React" }));

    // Byt steg mitt i redigeringen och kom tillbaka.
    await user.click(within(rail).getByRole("button", { name: /^Uppgifter/ }));
    await user.click(within(rail).getByRole("button", { name: /^Kompetenser/ }));

    // Chippet finns kvar — originalet är orört.
    expect(screen.getByText("React")).toBeInTheDocument();
  });
});

describe("CvCompleteGuide — ingen yta får DÖLJA ett blockerande fel (#815, rond-3 Major)", () => {
  // Parsern gissar aldrig datum (DQ3-3a), så VARJE parsad erfarenhet saknar startdatum
  // och är blockerande vid första submit. Korten seedas kollapsade (bekräfta-mönstret),
  // och routeToError expanderade bara FÖRSTA felet — så ett CV med två erfarenheter
  // visade ett fel, medan railen sa "2 fel" och foten sa "De är markerade nedan".
  // De kollapsade korten visade dessutom en lugn, neutral "Bekräfta datum"-pill om
  // poster som faktiskt hindrade sparandet. Expansionen är nu HÄRLEDD ur samma parse.
  it("två parsade erfarenheter → BÅDA felen renderas, inte bara det första", async () => {
    const user = userEvent.setup();
    renderGuide(
      makeContent({
        experiences: [
          {
            title: "Operatör",
            organization: "Verkstaden AB",
            period: "2005 - 2010",
            rawText: "Operatör",
          },
          {
            title: "Tekniker",
            organization: "Beta AB",
            period: "2010 - nu",
            rawText: "Tekniker",
          },
        ],
      }),
    );

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Spara/ }));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    // Fokus routas till steg 2. Båda posternas startdatum-fel måste vara SYNLIGA —
    // annars påstår foten att felen är markerade nedan medan de är gömda i kollapsade kort.
    await waitFor(() =>
      expect(
        document.getElementById("guide-exp-0-startDate-error"),
      ).not.toBeNull(),
    );
    expect(document.getElementById("guide-exp-1-startDate-error")).not.toBeNull();

    // Och de lugnande "Bekräfta datum"-pillarna sitter inte kvar på blockerande poster.
    expect(screen.queryByText("Bekräfta datum")).not.toBeInTheDocument();
  });

  // Bekräfta-raden bär en grön bock ("detta stämmer"). Den fick sitta kvar på ett fält
  // som blockerade sparandet — bocken ljög, och felet fanns ingenstans att se.
  it("ett ogiltigt kontaktfält faller ur bekräfta-läge och visar sitt fel", async () => {
    const user = userEvent.setup();
    const tooLongPhone = "0".repeat(60); // > 50 tecken → blockerande
    renderGuide(
      makeContent({
        contact: {
          fullName: "Anna Andersson",
          email: null,
          phone: tooLongPhone,
          location: null,
        },
      }),
    );

    // Telefonen hittades i filen → renderas i bekräfta-läge (grön bock + "Ändra").
    expect(
      screen.getByRole("button", { name: "Ändra Telefon" }),
    ).toBeInTheDocument();

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Spara/ }));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    // Efter submit får bocken inte stå kvar: fältet öppnas och bär sitt fel.
    await waitFor(() =>
      expect(document.getElementById("guide-pi-phone-error")).not.toBeNull(),
    );
    expect(
      screen.queryByRole("button", { name: "Ändra Telefon" }),
    ).not.toBeInTheDocument();
  });
});

describe("CvCompleteGuide — ytan slår inte igen när felet rättas (#815, rond-4 Major)", () => {
  // Den härledda expansionen var korrekt som SPÄRR (ingen yta får dölja ett fel) men
  // dubbelriktad: rättade användaren felet blev predikatet falskt och kortet kollapsade
  // MITT I inmatningen — fokus slängdes till <body>, och slutdatum/beskrivning gick inte
  // att nå. WCAG 3.2.2, på default-vägen (varje parsad post saknar startdatum).
  // Expansionen är nu en spärrhake: den kan växa, aldrig krympa.
  it("att fylla i startdatumet stänger INTE kortet och behåller fokus", async () => {
    const user = userEvent.setup();
    renderGuide(
      makeContent({
        experiences: [
          {
            title: "Operatör",
            organization: "Verkstaden AB",
            period: "2005 - 2010",
            rawText: "Operatör",
          },
          {
            title: "Tekniker",
            organization: "Beta AB",
            period: "2010 - nu",
            rawText: "Tekniker",
          },
        ],
      }),
    );

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Spara/ }));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    // Båda korten är öppna (spärren).
    await waitFor(() =>
      expect(document.getElementById("guide-exp-1-startDate")).not.toBeNull(),
    );

    // Rätta post 2 (den routeToError INTE hoppade till) — det är där buggen bodde.
    const startDate = document.getElementById(
      "guide-exp-1-startDate",
    ) as HTMLInputElement;
    startDate.focus();
    await user.type(startDate, "2018-01-01");

    // Kortet är kvar, fältet är kvar i DOM, och fokus ligger kvar i det.
    expect(document.getElementById("guide-exp-1-startDate")).not.toBeNull();
    expect(document.getElementById("guide-exp-1-description")).not.toBeNull();
    expect(document.activeElement).toBe(startDate);
  });

  it("att rätta ett kontaktfält stänger inte tillbaka det till bekräfta-läge", async () => {
    const user = userEvent.setup();
    const tooLongLocation = "x".repeat(210); // > 200 → blockerande
    renderGuide(
      makeContent({
        contact: {
          fullName: "Anna Andersson",
          email: null,
          phone: null,
          location: tooLongLocation,
        },
      }),
    );

    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    await user.click(within(rail).getByRole("button", { name: /^Spara/ }));
    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    await waitFor(() =>
      expect(document.getElementById("guide-pi-location")).not.toBeNull(),
    );

    const location = document.getElementById("guide-pi-location") as HTMLInputElement;
    location.focus();
    await user.clear(location);
    await user.type(location, "Göteborg");

    // Fältet finns kvar (faller inte tillbaka till bekräfta-raden) och behåller fokus.
    expect(document.getElementById("guide-pi-location")).not.toBeNull();
    expect(document.activeElement).toBe(location);
  });
});

describe("CvCompleteGuide — markeringen skriver inte över statusen (#815, design Blocker)", () => {
  // Axlarna delades korrekt i JSX (data-state = var jag står, data-status = vad steget
  // säger) — men CSS:en slog ihop dem igen via SPECIFICITET: en descendant-selektor på
  // [data-state="active"] vägde tyngre än [data-status="done"]. Följden i ljust tema:
  // bocken färgades accent-700, som är SAMMA hex som accent-800-fyllningen → mörkgrön
  // bock på mörkgrön skiva (1,0:1, glyfen borta), och varningstriangeln blev GRÖN på
  // det enda steg som faktiskt blockerar sparandet.
  //
  // jsdom applicerar ingen CSS, så testet pinnar KONTRAKTET selektorerna vilar på:
  // indikatorn behåller sin status även när steget är aktivt, och glyfen byts inte ut.
  function railStep(name: RegExp) {
    const rail = screen.getByRole("navigation", { name: "Steg i guiden" });
    return within(rail).getByRole("button", { name });
  }

  it("ett aktivt KLART steg behåller data-status='done' (och sin bock)", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent({ skills: ["React"], languages: ["Svenska"] }));

    // Kompetenser är klart (båda uppgifterna fyllda, inga fel). Gå dit → aktivt + klart.
    const step = railStep(/^Kompetenser/);
    await user.click(step);

    const active = railStep(/^Kompetenser/);
    expect(active).toHaveAttribute("data-state", "active");
    expect(active).toHaveAttribute("data-status", "done");

    const indicator = active.querySelector(".jp-guide__railind");
    expect(indicator).not.toBeNull();
    // Statusen överlever markeringen — annars kan CSS:en aldrig färga bocken rätt.
    expect(indicator).toHaveAttribute("data-status", "done");
    // Och glyfen är fortfarande bocken, inte stegsiffran.
    expect(indicator!.querySelector("svg")).not.toBeNull();
    expect(indicator!.textContent).not.toContain("3");
  });

  it("ett aktivt BLOCKERANDE steg behåller data-status='attention' (och sin varning)", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent());

    // Skapa en blockerare på steg 2 (tom appendad post) och stanna kvar där.
    await user.click(railStep(/erfarenhet/i));
    await user.click(screen.getByRole("button", { name: /Lägg till erfarenhet/ }));

    const active = railStep(/erfarenhet/i);
    expect(active).toHaveAttribute("data-state", "active");
    expect(active).toHaveAttribute("data-status", "attention");

    const indicator = active.querySelector(".jp-guide__railind");
    // Varningen får inte skrivas över av markeringen — det steget BLOCKERAR sparandet
    // och får aldrig bära produktens success-färg.
    expect(indicator).toHaveAttribute("data-status", "attention");
  });
});

describe("CvCompleteGuide — yrkesstyrda sektionsförslag (8b.4a, ADR 0107)", () => {
  async function gotoSections(user: ReturnType<typeof userEvent.setup>) {
    await user.click(
      screen.getByRole("button", { name: /Erfarenhet och utbildning/ }),
    );
  }

  it("föreslår yrkets sektioner med kunskapsbankens motivering, och markerar standardsektionen", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent(), VARD_SUGGESTIONS);
    await gotoSections(user);

    expect(screen.getByText("Föreslagna sektioner")).toBeInTheDocument();
    // Motiveringen kommer från assetet, inte från i18n — samma proveniens-regel som
    // ProposedChange.rationale (ingen prosa som motorn hittat på).
    expect(screen.getByText("Vanligt inom vård och omsorg")).toBeInTheDocument();

    // Extra standardsektionen bär "Rekommenderas" i TEXT, aldrig i färg ensam (WCAG 1.4.1).
    const legitimation = screen.getByRole("button", {
      name: /Lägg till sektionen Legitimation och intyg/,
    });
    expect(within(legitimation).getByText("Rekommenderas")).toBeInTheDocument();

    // De bara-vanliga bär den inte.
    const korkort = screen.getByRole("button", {
      name: /Lägg till sektionen Körkort/,
    });
    expect(within(korkort).queryByText("Rekommenderas")).not.toBeInTheDocument();
  });

  it("lägger till sektionen med backendens rubrik när ett förslag klickas — och förslaget försvinner", async () => {
    const user = userEvent.setup();
    renderGuide(makeContent(), VARD_SUGGESTIONS);
    await gotoSections(user);

    await user.click(
      screen.getByRole("button", { name: /Lägg till sektionen Legitimation och intyg/ }),
    );

    // Rubriken skrivs in EXAKT som backend angav den. Den är inte kosmetika: en rubrik
    // segmenteraren inte känner igen får sin text uppslukad av föregående sektion vid
    // nästa import (#815). FE hittar därför aldrig på en egen.
    const heading = document.querySelector<HTMLInputElement>(
      "#guide-section-0-heading",
    );
    expect(heading?.value).toBe("Legitimation och intyg");

    // Förslaget är borta — härlett ur live-formuläret, inte ur ett separat "tillagt"-state
    // som kunde divergera.
    expect(
      screen.queryByRole("button", { name: /Lägg till sektionen Legitimation och intyg/ }),
    ).not.toBeInTheDocument();
    // Syskonen står kvar.
    expect(
      screen.getByRole("button", { name: /Lägg till sektionen Körkort/ }),
    ).toBeInTheDocument();
  });

  it("frågar efter yrket NÄR det saknas — generisk rad + väg in i matchningsinställningarna", async () => {
    // Tomt läge (1): hon har aldrig sagt vad hon jobbar med (handoff-regel (d)).
    const user = userEvent.setup();
    renderGuide(makeContent(), {
      branschgrupp: "ovriga",
      hasOccupationPreference: false,
      rationale: "Vanliga sektioner i svenska CV",
      suggestions: [
        { sectionId: "kurser", heading: "Kurser", isStandard: false },
        { sectionId: "korkort", heading: "Körkort", isStandard: false },
      ],
    });
    await gotoSections(user);

    // Övriga är en FÖRSTKLASSIG rad, inte ett hål — förslagen finns.
    expect(
      screen.getByRole("button", { name: /Lägg till sektionen Kurser/ }),
    ).toBeInTheDocument();
    // …och hon får vägen in till att förbättra dem.
    expect(
      screen.getByRole("link", { name: "Till inställningar" }),
    ).toHaveAttribute("href", "/installningar#matchning");
  });

  it("frågar INTE efter yrket när hon redan angett ett som landar i Övriga", async () => {
    // Tomt läge (2), 62,1 %-majoriteten. SAMMA branschgrupp som testet ovan, SAMMA förslag
    // — bara flaggan skiljer. Det är hela poängen: om de två lägena slogs ihop till ett
    // "är det Övriga?"-villkor vore det här testet och det förra omöjliga att skilja åt,
    // och hon skulle få frågan om sitt yrke igen trots att hon redan svarat.
    const user = userEvent.setup();
    renderGuide(makeContent(), {
      branschgrupp: "ovriga",
      hasOccupationPreference: true,
      rationale: "Vanliga sektioner i svenska CV",
      suggestions: [
        { sectionId: "kurser", heading: "Kurser", isStandard: false },
        { sectionId: "korkort", heading: "Körkort", isStandard: false },
      ],
    });
    await gotoSections(user);

    expect(
      screen.getByRole("button", { name: /Lägg till sektionen Kurser/ }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Till inställningar" }),
    ).not.toBeInTheDocument();
  });

  it("renderar den generiska panelen oförändrad när förslagen inte kunde hämtas", async () => {
    // Förslagen är RÅDGIVANDE. En trasig förslagsrad får aldrig blockera det som faktiskt
    // är uppgiften — att slutföra CV:t. Fri rubrik finns kvar, precis som före 8b.4a.
    const user = userEvent.setup();
    renderGuide(makeContent(), null);
    await gotoSections(user);

    expect(screen.queryByText("Föreslagna sektioner")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Lägg till sektion$/ }));
    const heading = document.querySelector<HTMLInputElement>(
      "#guide-section-0-heading",
    );
    expect(heading).not.toBeNull();
    expect(heading?.value).toBe("");
  });

  it("tar bort chippet när användaren själv skriver in samma rubrik — härlett, inte bokfört", async () => {
    // Bevisar HÄRLEDNINGEN, inte bara resultatet. Det förra testet här filtrerade bort `kurser`
    // ur sin EGEN input-payload och asserterade sedan att `kurser` inte renderades — det testade
    // sin fixture. Mekanismen det krediterade (synonym-filtrering) ägs dessutom av servern.
    //
    // Det här testet kan bara passera om chip-listan verkligen härleds ur live-formuläret: ett
    // `useState`-baserat "tillagda"-Set hade renderat chippet vidare, för användaren klickade
    // aldrig på det.
    const user = userEvent.setup();
    renderGuide(makeContent(), VARD_SUGGESTIONS);
    await gotoSections(user);

    expect(
      screen.getByRole("button", { name: /Lägg till sektionen Körkort/ }),
    ).toBeInTheDocument();

    // Egen sektion, egen handpåläggning — exakt samma rubrik som chippet bär.
    await user.click(screen.getByRole("button", { name: /Lägg till sektion$/ }));
    const heading = document.querySelector<HTMLInputElement>("#guide-section-0-heading");
    await user.type(heading!, "Körkort");

    expect(
      screen.queryByRole("button", { name: /Lägg till sektionen Körkort/ }),
    ).not.toBeInTheDocument();
  });

  it("återför chippet när sektionen tas bort igen", async () => {
    // Den andra riktningen, och den som skiljer en härledning från ett bokfört Set: ångrar hon
    // tillägget ska förslaget komma TILLBAKA. Ett `added`-Set hade lämnat chippet borta för
    // alltid — ett dött förslag hon aldrig kan få igen.
    const user = userEvent.setup();
    renderGuide(makeContent(), VARD_SUGGESTIONS);
    await gotoSections(user);

    await user.click(
      screen.getByRole("button", { name: /Lägg till sektionen Körkort/ }),
    );
    expect(
      screen.queryByRole("button", { name: /Lägg till sektionen Körkort/ }),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Ta bort sektion 1/ }));

    expect(
      screen.getByRole("button", { name: /Lägg till sektionen Körkort/ }),
    ).toBeInTheDocument();
  });

  it("flyttar fokus till den nya rubriken när ett förslag läggs till", async () => {
    // Chippet avmonteras av sitt eget klick. Utan fokusflytt landar fokus på <body> och
    // tangentbordsanvändaren tappar sin plats utan besked (WCAG 2.4.3).
    const user = userEvent.setup();
    renderGuide(makeContent(), VARD_SUGGESTIONS);
    await gotoSections(user);

    await user.click(
      screen.getByRole("button", { name: /Lägg till sektionen Legitimation och intyg/ }),
    );

    await waitFor(() =>
      expect(document.activeElement).toBe(
        document.querySelector("#guide-section-0-heading"),
      ),
    );
  });

  it("frågar efter yrket ÄVEN när alla generiska förslag redan finns i CV:t", async () => {
    // Regel (d) får inte dö tyst. Har hon inget yrke angivet OCH bär CV:t redan alla Övriga-
    // sektionerna blir förslags-listan tom — och då är prompten det ENDA som är kvar att visa.
    // Utan det här testet kunde render-grinden reduceras till `openSuggestions.length > 0` utan
    // att något gick rött (mutationen överlevde: test-writer-fynd), och hon hade aldrig fått
    // frågan om sitt yrke.
    const user = userEvent.setup();
    renderGuide(
      makeContent({
        sections: [
          { heading: "Kurser", entries: [] },
          { heading: "Körkort", entries: [] },
        ],
      }),
      {
        branschgrupp: "ovriga",
        hasOccupationPreference: false,
        rationale: "Vanliga sektioner i svenska CV",
        // Servern har redan filtrerat bort det CV:t bär → tom lista.
        suggestions: [],
      },
    );
    await gotoSections(user);

    expect(screen.queryByText("Föreslagna sektioner")).not.toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Till inställningar" }),
    ).toHaveAttribute("href", "/installningar#matchning");
  });
});
