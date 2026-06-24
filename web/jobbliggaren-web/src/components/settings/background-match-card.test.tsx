import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

// Mock server-actionen (server-actions körs aldrig i jsdom). Kortet importerar
// updateNotificationConsentAction från @/lib/actions/me.
const { consentMock } = vi.hoisted(() => ({ consentMock: vi.fn() }));
vi.mock("@/lib/actions/me", () => ({
  updateNotificationConsentAction: consentMock,
}));

import { BackgroundMatchCard } from "./background-match-card";

function renderCard(
  overrides?: Partial<React.ComponentProps<typeof BackgroundMatchCard>>
) {
  return render(
    <BackgroundMatchCard
      initialEnabled={false}
      initialCadence="Weekly"
      {...overrides}
    />
  );
}

beforeEach(() => {
  consentMock.mockReset();
  consentMock.mockResolvedValue({ success: true });
});

describe("BackgroundMatchCard — pre-fill + grundläge", () => {
  it("default OFF: toggeln är av och kadens-väljaren är inaktiverad", () => {
    renderCard();
    const toggle = screen.getByRole("switch", {
      name: "Matcha nya annonser åt mig",
    });
    expect(toggle).toHaveAttribute("aria-checked", "false");

    // Kadens-radiogruppen finns men är inaktiverad (a11y: läget annonseras,
    // döljs inte). Båda radio-optionerna är disabled.
    const group = screen.getByRole("radiogroup", {
      name: "Hur ofta vill du få sammanfattningen",
    });
    const radios = within(group).getAllByRole("radio");
    for (const r of radios) expect(r).toBeDisabled();
    // Inaktiverad hjälptext förklarar att man måste slå på notiserna först.
    expect(
      screen.getByText(/Slå på matchningsnotiser för att välja/)
    ).toBeInTheDocument();
  });

  it("pre-fill ON + Daily: toggeln är på, kadens-väljaren aktiv med Dagligen vald", () => {
    renderCard({ initialEnabled: true, initialCadence: "Daily" });
    expect(
      screen.getByRole("switch", { name: "Matcha nya annonser åt mig" })
    ).toHaveAttribute("aria-checked", "true");

    const group = screen.getByRole("radiogroup", {
      name: "Hur ofta vill du få sammanfattningen",
    });
    const daily = within(group).getByRole("radio", { name: "Dagligen" });
    const weekly = within(group).getByRole("radio", { name: "Veckovis" });
    expect(daily).not.toBeDisabled();
    expect(daily).toHaveAttribute("aria-checked", "true");
    expect(weekly).toHaveAttribute("aria-checked", "false");
    // Aktiv hjälptext (inte den inaktiverade varianten).
    expect(
      screen.getByText(/Gäller sammanfattningen av starka matchningar/)
    ).toBeInTheDocument();
  });

  it("ärlig framing: introt nämner matchningslistan, aldrig e-post", () => {
    renderCard();
    const intro = screen.getByText(/matchar vi nya annonser mot din profil/);
    expect(intro).toHaveTextContent(/matchningslista/);
    expect(intro.textContent ?? "").not.toMatch(/e-post|mejl|mail/i);
  });
});

describe("BackgroundMatchCard — opt-in/opt-out save", () => {
  it("slå på toggeln skickar {enabled:true, cadence:Weekly} och aktiverar väljaren", async () => {
    const user = userEvent.setup();
    renderCard(); // default OFF + Weekly

    await user.click(
      screen.getByRole("switch", { name: "Matcha nya annonser åt mig" })
    );

    await waitFor(() => expect(consentMock).toHaveBeenCalledTimes(1));
    expect(consentMock).toHaveBeenCalledWith({
      enabled: true,
      cadence: "Weekly",
    });
    // Kadens-väljaren blir aktiv direkt (optimistiskt).
    const group = screen.getByRole("radiogroup", {
      name: "Hur ofta vill du få sammanfattningen",
    });
    expect(within(group).getByRole("radio", { name: "Veckovis" })).not.toBeDisabled();
  });

  it("byt kadens när på skickar {enabled:true, cadence:Daily}", async () => {
    const user = userEvent.setup();
    renderCard({ initialEnabled: true, initialCadence: "Weekly" });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    await waitFor(() => expect(consentMock).toHaveBeenCalledTimes(1));
    expect(consentMock).toHaveBeenCalledWith({
      enabled: true,
      cadence: "Daily",
    });
  });

  it("opt-out (slå av) skickar {enabled:false} och bevarar kadensen i bodyn", async () => {
    const user = userEvent.setup();
    renderCard({ initialEnabled: true, initialCadence: "Daily" });

    await user.click(
      screen.getByRole("switch", { name: "Matcha nya annonser åt mig" })
    );

    await waitFor(() => expect(consentMock).toHaveBeenCalledTimes(1));
    expect(consentMock).toHaveBeenCalledWith({
      enabled: false,
      cadence: "Daily",
    });
    // Väljaren blir inaktiverad igen efter opt-out.
    const group = screen.getByRole("radiogroup", {
      name: "Hur ofta vill du få sammanfattningen",
    });
    for (const r of within(group).getAllByRole("radio")) {
      expect(r).toBeDisabled();
    }
  });

  it("lyckad save visar status-raden 'Sparat HH:mm'", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(
      screen.getByRole("switch", { name: "Matcha nya annonser åt mig" })
    );

    expect(await screen.findByText(/^Sparat \d{2}:\d{2}$/)).toBeInTheDocument();
  });

  it("misslyckad save återställer toggeln och visar role=alert", async () => {
    const user = userEvent.setup();
    consentMock.mockResolvedValue({ success: false, error: "nej" });
    renderCard(); // OFF

    await user.click(
      screen.getByRole("switch", { name: "Matcha nya annonser åt mig" })
    );

    // Toggeln reverteras till av + alert syns.
    await waitFor(() =>
      expect(
        screen.getByRole("switch", { name: "Matcha nya annonser åt mig" })
      ).toHaveAttribute("aria-checked", "false")
    );
    expect(screen.getByRole("alert")).toHaveTextContent("nej");
  });
});
