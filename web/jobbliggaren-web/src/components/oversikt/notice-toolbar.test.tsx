import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NoticeToolbar } from "./notice-toolbar";

// Uppdatera-kontrollen kör `router.refresh()`; utan mock kastar next/navigation utanför
// en App Router-kontext (samma mönster som recent-search-row.test.tsx).
const refreshMock = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: refreshMock }),
}));

const ISO = "2026-07-19T06:05:00.000Z";

/** Alla tester ger samma ISO-instant; bara den formaterade tiden varierar. */
function renderToolbar(lastUpdated: string) {
  return render(<NoticeToolbar lastUpdated={lastUpdated} lastUpdatedIso={ISO} />);
}

describe("NoticeToolbar — stämpelns betydelse (#1572)", () => {
  beforeEach(() => {
    window.localStorage.clear();
    refreshMock.mockClear();
  });

  it("contentCanChange={false} stämplar sidladdningen, inte datafärskhet", () => {
    // På en yta vars innehåll är fryst är "Senast uppdaterad" ett påstående ingen
    // render kan leverera. Raden ska då säga det som faktiskt hände.
    render(
      <NoticeToolbar
        lastUpdated="08:05"
        lastUpdatedIso={ISO}
        contentCanChange={false}
      />,
    );

    expect(screen.getByText(/Sidan laddades/)).toBeInTheDocument();
    expect(screen.queryByText(/Senast uppdaterad/)).toBeNull();
    // Kontrollen står kvar: en omladdning är en verklig händelse även på fryst data.
    expect(screen.getByRole("button", { name: "Uppdatera" })).toBeInTheDocument();
  });

  it("default bär kvar app-påståendet (kontrafaktum)", () => {
    render(<NoticeToolbar lastUpdated="08:05" lastUpdatedIso={ISO} />);

    expect(screen.getByText(/Senast uppdaterad/)).toBeInTheDocument();
    expect(screen.queryByText(/Sidan laddades/)).toBeNull();
  });
});

describe("NoticeToolbar", () => {
  beforeEach(() => {
    window.localStorage.clear();
    refreshMock.mockClear();
  });

  it("renderar stämpeln som enbart klockslag i ett <time dateTime> (#1556)", () => {
    // Datumet lämnade den SYNLIGA ytan men inte DOM:en: utan `dateTime` går en flik som
    // stått öppen över midnatt inte att adjudicera, och då är tiden ensam en gissning.
    const { container } = renderToolbar("08:05");
    expect(screen.getByText(/Senast uppdaterad/)).toBeInTheDocument();
    const stamp = container.querySelector("time");
    expect(stamp).not.toBeNull();
    expect(stamp).toHaveTextContent("08:05");
    expect(stamp).toHaveAttribute("dateTime", ISO);
    expect(screen.queryByText(/2026-07-19/)).toBeNull();
  });

  it("visar en uppdatera-kontroll bredvid stämpeln, alltid (#1549)", () => {
    // Villkoret Klas satte: en tidpunkt användaren inte kan påverka ska bort. Kontrollen
    // är därför INTE villkorad på att det finns notiser — den hör till stämpeln.
    renderToolbar("08:05");
    expect(screen.getByRole("button", { name: "Uppdatera" })).toBeEnabled();
  });

  it("kontrollen är ikon-only och får sitt namn ur aria-label (DESIGN.md §6)", () => {
    // Ikonen är aria-hidden, så utan aria-label har knappen inget tillgängligt namn alls.
    renderToolbar("08:05");
    const button = screen.getByRole("button", { name: "Uppdatera" });
    expect(button).toHaveAttribute("aria-label", "Uppdatera");
    expect(button).toHaveAttribute("title", "Uppdatera");
    expect(button.textContent).toBe("");
  });

  it("klick begär en ny render av sidan", async () => {
    // `/oversikt` är force-dynamic, så en refresh ger ett nytt `new Date()` och därmed
    // en ny stämpel — kontrollen behöver ingen egen hämtväg.
    const user = userEvent.setup();
    renderToolbar("x");

    await user.click(screen.getByRole("button", { name: "Uppdatera" }));

    expect(refreshMock).toHaveBeenCalledTimes(1);
  });

  it("vänstergruppen bär stämpeln, Uppdatera och kvittot", () => {
    // Assertionen är STRUKTURELL, inte bara närvaro. Kvittot måste ligga i samma grupp,
    // omedelbart efter kontrollen — #1556:s bind, som överlever att #1557 flyttade
    // "Markera alla" ur toolbaren: den får fortfarande inte skjuta in något emellan.
    const { container } = renderToolbar("08:05");
    const left = container.querySelector<HTMLElement>(
      ".jp-oversikt-toolbar__left",
    );
    expect(left).not.toBeNull();

    expect(within(left!).getByRole("button", { name: "Uppdatera" })).toBeInTheDocument();
    expect(within(left!).getByText(/08:05/)).toBeInTheDocument();
    expect(within(left!).getByRole("status")).toBeInTheDocument();

    const button = within(left!).getByRole("button", { name: "Uppdatera" });
    expect(button.nextElementSibling).toBe(within(left!).getByRole("status"));
  });

  it("kvitto-regionen är monterad även när den är tom", () => {
    // En live-region som monteras samtidigt som sitt innehåll annonseras opålitligt.
    // Villkorad rendering av spannet skulle alltså tysta kvittot för en skärmläsare.
    renderToolbar("08:05");
    const region = screen.getByRole("status");
    expect(region.textContent).toBe("");
  });

  it("kvittot hamnar i live-regionen efter en uppdatering (#1549, #1556)", async () => {
    // Stämpeln har minutupplösning och en refresh går på under en sekund, så två klick
    // i samma minut lämnar den oförändrad. Utan kvitto ser kontrollen verkningslös ut
    // (design-reviewer Major). Sedan kontrollen blev ikon-only kan kvittot inte bo i
    // knappens etikett; regionen är dess enda hem, och knappens namn står stilla.
    const user = userEvent.setup();
    renderToolbar("x");

    const button = screen.getByRole("button", { name: "Uppdatera" });
    await user.click(button);

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent("Uppdaterad"),
    );
    expect(button).toHaveAttribute("aria-label", "Uppdatera");
  });

  it("blir ALDRIG disabled — den är idempotent och får inte tappa fokus", async () => {
    // design-reviewer mätte i Chromium att `disabled` på den just aktiverade knappen
    // kastar fokus till <body> och aldrig ger tillbaka det. Husets form för idempotenta
    // kontroller är att inte disabla alls (company-follow-button.tsx, Klas PR5).
    const user = userEvent.setup();
    renderToolbar("x");
    const button = screen.getByRole("button", { name: "Uppdatera" });

    await user.click(button);

    expect(button).not.toBeDisabled();
    expect(document.activeElement).toBe(button);
  });
});
