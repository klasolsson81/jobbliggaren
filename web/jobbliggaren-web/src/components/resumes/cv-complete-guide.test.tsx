import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvCompleteGuide } from "./cv-complete-guide";
import type {
  ParsedContentDto,
  ParseConfidenceDto,
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
    contact: { fullName: "Anna Andersson", email: null, phone: null, location: null },
    profile: null,
    experiences: [],
    educations: [],
    skills: [],
    languages: [],
    ...overrides,
  };
}

function renderGuide(content: ParsedContentDto) {
  return render(
    <CvCompleteGuide
      parsedId={PARSED_ID}
      sourceFileName="cv.pdf"
      content={content}
      confidence={CONFIDENCE}
    />,
  );
}

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
