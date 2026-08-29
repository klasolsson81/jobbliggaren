import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { isProtectedPath } from "@/lib/auth/protected-routes";
import { daysSince } from "@/lib/i18n/relative-time";
import { GUEST_MOCK, GUEST_MOCK_REF_DATE } from "@/lib/guest/mock-data";
import { GuestOversiktPage } from "./guest-oversikt-page";

// Sidan renderar NoticeToolbar, vars uppdatera-kontroll kallar `useRouter()` (#1549).
// Utan mock kastar next/navigation "invariant expected app router to be mounted" —
// samma mock, av samma skäl, som `oversikt-page.test.tsx:20-24`.
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn() }),
}));

const PREFS_KEY = "jp-oversikt-notice-prefs";

beforeEach(() => {
  window.localStorage.clear();
});

/**
 * #1572 — gästöversikten komponerades om på appens nuvarande Översikt.
 *
 * Testerna nedan pinnar de egenskaper som INTE syns i en diff: att ytan slutade
 * bära den layout appen lämnade i #726, att den inte längre lovar något den inte
 * kan hålla, och att den inte längre skriver eller läser i den inloggade appens
 * notis-inställningar.
 */
describe("GuestOversiktPage — komposition (#1572)", () => {
  it("heroet bär ingen aside", () => {
    // #726 tog bort I dag-kortet ur appens hero; gästen var sista ytan som hade
    // kvar det. Faller om asiden monteras igen.
    const { container } = render(<GuestOversiktPage />);

    expect(container.querySelector(".jp-pagehero__aside")).toBeNull();
  });

  it("renderar de tre källsektionerna appen har", () => {
    render(<GuestOversiktPage />);

    for (const name of ["Mina ansökningar", "Jobbannonser", "Företagsbevakning"]) {
      expect(screen.getByRole("region", { name })).toBeInTheDocument();
    }
  });

  it("sammanfattningarna sitter INUTI sina sektioner", () => {
    // Komponenternas egna tester målar dem isolerat och skulle förbli gröna om
    // `summary`-propen togs bort här. Det här asserterar inkopplingen.
    render(<GuestOversiktPage />);

    const apps = screen.getByRole("region", { name: "Mina ansökningar" });
    expect(
      within(apps).getByRole("list", { name: "Ansökningar per steg" }),
    ).toBeInTheDocument();

    const companies = screen.getByRole("region", { name: "Företagsbevakning" });
    // 14 + 9 + 0 aktiva annonser över tre bevakningar.
    expect(
      within(companies).getByText("3 bevakade företag · 23 aktiva annonser"),
    ).toBeInTheDocument();
  });

  it("'Markera alla som lästa' ligger EFTER sista sektionen i DOM-ordning", () => {
    // Placeringen ÄR #1557:s defekt, och den är opinnad av allt annat: en flytt
    // tillbaka upp ger noll signal från tsc, lint och resten av sviten.
    const { container } = render(<GuestOversiktPage />);

    const row = container.querySelector(".jp-notice-bulk");
    expect(row).not.toBeNull();

    const sections = [...container.querySelectorAll("section.jp-section")];
    expect(sections).toHaveLength(3);
    const last = sections[sections.length - 1]!;

    // DOCUMENT_POSITION_FOLLOWING sätts även för en DESCENDANT (4|16), så `contains`
    // skär bort inneslutning — paret betyder syskon EFTER.
    expect(
      last.compareDocumentPosition(row!) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(last.contains(row!)).toBe(false);
  });
});

describe("GuestOversiktPage — den okvalificerade knappen (#1572)", () => {
  it("hinten är kopplad till knappen och gör INGET dygnspåstående", () => {
    // Hela issuet: appens hint säger "till i morgon", vilket är sant där för att
    // notis-id:na bär en datum-slug. Gästens id är statiska literaler som aldrig
    // roterar. Testet faller om sidan slutar skicka `noticeIdsRotate={false}`.
    const { container } = render(<GuestOversiktPage />);

    const button = screen.getByRole("button", { name: /Markera alla som lästa/ });
    const hintId = button.getAttribute("aria-describedby");
    expect(hintId).not.toBeNull();

    const hint = container.querySelector(`#${hintId}`);
    expect(hint).not.toBeNull();
    expect(hint).toHaveTextContent(/Ingenting tas bort/);
    expect(hint).not.toHaveTextContent(/till i morgon/);
  });

  it("namnger antalet i live-regionen efter klicket (WCAG 4.1.3)", async () => {
    const user = userEvent.setup();
    const { container } = render(<GuestOversiktPage />);

    // Skopat till bulk-raden: sidan bär TVÅ live-regioner, för toolbarens
    // uppdatera-kvitto är också en `role="status"`.
    const row = () => within(container.querySelector<HTMLElement>(".jp-notice-bulk")!);

    // Regionen är monterad före klicket — en live-region som monteras samtidigt
    // som sitt innehåll annonseras opålitligt (#1549/#1556).
    expect(row().getByRole("status")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Markera alla som lästa/ }));

    expect(row().getByRole("status")).toHaveTextContent(
      "4 notiser markerade som lästa",
    );
  });

  it("hinten håller: en avfärdad notis går att ta tillbaka", async () => {
    // "Ingenting tas bort" var FALSKT på den gamla gästytan — `<NoticeList>` hade
    // inget läst-läge alls, så ett klick tömde demot permanent i den webbläsaren.
    // Återvändbarheten är alltså en förutsättning för copyn, inte en extra finess.
    const user = userEvent.setup();
    render(<GuestOversiktPage />);

    await user.click(screen.getByRole("button", { name: /Markera alla som lästa/ }));

    const jobads = screen.getByRole("region", { name: "Jobbannonser" });
    expect(within(jobads).getByText("1 läst notis")).toBeInTheDocument();

    await user.click(within(jobads).getByRole("button", { name: "Visa" }));
    await user.click(
      within(jobads).getByRole("button", { name: "Återställ notis" }),
    );

    expect(within(jobads).getByText("1 oläst")).toBeInTheDocument();
  });
});

describe("GuestOversiktPage — ytan skriver inte i den inloggade appen (#1572)", () => {
  it("renderar inget kugghjul", () => {
    render(<GuestOversiktPage />);

    expect(
      screen.queryByRole("button", { name: "Notisinställningar" }),
    ).toBeNull();
  });

  it("en avstängd notistyp i den DELADE storen filtrerar INTE demot", () => {
    // `jp-oversikt-notice-prefs` har formen `"<källa>:<typ>"` och delas med appen.
    // Utan `<InertNoticePrefsProvider>` hade den här seedningen tagit bort
    // erbjudande-notisen från en publik yta där besökaren varken kan se eller
    // ångra filtreringen (CTO-dom 2026-08-29).
    window.localStorage.setItem(
      PREFS_KEY,
      JSON.stringify({ "applications:offers": false }),
    );

    const { container } = render(<GuestOversiktPage />);

    expect(container.querySelector(".jp-notice--success")).not.toBeNull();
    const apps = screen.getByRole("region", { name: "Mina ansökningar" });
    expect(within(apps).getByText("2 olästa")).toBeInTheDocument();
  });
});

describe("GuestOversiktPage — inga länkar in i den skyddade appen (#1572)", () => {
  it("ingen renderad href matchar PROTECTED_PREFIXES", () => {
    // Läser SSOT:en, inte en lista av dagens fyra fällor: `/ansokningar`,
    // `/ny-ansokan`, `/foretag/bevakade` och `/foretag/sok` bor i de delade
    // sammanfattningarna, och proxyn skickar en utloggad besökare till
    // `/logga-in`. Testet växer med appen.
    const { container } = render(<GuestOversiktPage />);

    const internal = [...container.querySelectorAll("a[href]")]
      .map((a) => a.getAttribute("href")!)
      .filter((href) => href.startsWith("/"));

    expect(internal.length).toBeGreaterThan(0);
    expect(internal.filter((href) => isProtectedPath(href))).toEqual([]);
  });
});

describe("GuestOversiktPage — relativ tid (#1516)", () => {
  it("notisernas tid är HÄRLEDD ur mocken, inte ett valt ord", () => {
    // #1516:s renderställe flyttade hit när summary-rutnätet gick: notisernas
    // tidkolumn räknas nu ur `updatedAtIso`, som appen gör. Förväntningarna
    // beräknas ur mocken — hårdkodas paret i sidan OCH mockens datum flyttas,
    // faller testet.
    const sv = { today: "idag", yesterday: "igår" };
    const expected = (iso: string) => {
      const d = daysSince(iso, GUEST_MOCK_REF_DATE);
      return d <= 0 ? sv.today : d === 1 ? sv.yesterday : `${d} dagar sedan`;
    };
    const offer = GUEST_MOCK.applications.find((a) => a.status === "Offer")!;
    const interview = GUEST_MOCK.applications.find(
      (a) => a.status === "Interview",
    )!;
    // Paret är bärande: samma ord på båda hade passerat en enskild assertion.
    expect(expected(offer.updatedAtIso)).not.toBe(
      expected(interview.updatedAtIso),
    );

    const { container } = render(<GuestOversiktPage />);

    expect(
      container
        .querySelector(".jp-notice--success")
        ?.querySelector(".jp-notice__time")?.textContent,
    ).toBe(expected(offer.updatedAtIso));
    expect(
      container
        .querySelector(".jp-notice--brand")
        ?.querySelector(".jp-notice__time")?.textContent,
    ).toBe(expected(interview.updatedAtIso));
  });

  it("renderar ingen `för …`-form någonstans på sidan", () => {
    const { container } = render(<GuestOversiktPage />);

    expect(container.textContent).not.toMatch(/för \d/);
  });
});
