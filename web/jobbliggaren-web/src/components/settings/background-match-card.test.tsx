import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";

// Mock server-actionen (server-actions körs aldrig i jsdom). Kortet importerar
// updateNotificationConsentAction från @/lib/actions/me.
const { consentMock } = vi.hoisted(() => ({ consentMock: vi.fn() }));
vi.mock("@/lib/actions/me", () => ({
  updateNotificationConsentAction: consentMock,
}));

import { BackgroundMatchCard } from "./background-match-card";
import type { DigestCadence } from "@/lib/dto/me";

/**
 * Kadensen är KONTROLLERAD av SettingsForm (bevakning F4: den är delad med
 * följ-notis-kortet, ADR 0087 D2). Testet speglar den ägaren med en minimal
 * host — annars skulle `value` aldrig ändras efter ett klick och testet mäta
 * en verklighet som inte finns.
 */
function TestHost({
  initialEnabled = false,
  initialCadence = "Weekly",
  followEnabled = false,
}: {
  initialEnabled?: boolean;
  initialCadence?: DigestCadence;
  followEnabled?: boolean;
}) {
  const [cadence, setCadence] = useState<DigestCadence>(initialCadence);
  return (
    <BackgroundMatchCard
      initialEnabled={initialEnabled}
      cadence={cadence}
      onCadenceChange={setCadence}
      followEnabled={followEnabled}
    />
  );
}

function renderCard(overrides?: React.ComponentProps<typeof TestHost>) {
  return render(<TestHost {...overrides} />);
}

const CADENCE_GROUP = "Hur ofta vill du få sammanfattningen";
const TOGGLE = "Matcha nya annonser åt mig";

beforeEach(() => {
  consentMock.mockReset();
  consentMock.mockResolvedValue({ success: true });
});

describe("BackgroundMatchCard — pre-fill + grundläge", () => {
  it("default OFF (båda kanalerna av): toggeln är av och kadens-väljaren är inaktiverad", () => {
    renderCard();
    expect(screen.getByRole("switch", { name: TOGGLE })).toHaveAttribute(
      "aria-checked",
      "false"
    );

    // Kadens-radiogruppen finns men är inaktiverad (a11y: läget annonseras,
    // döljs inte). Båda radio-optionerna är disabled.
    const group = screen.getByRole("radiogroup", { name: CADENCE_GROUP });
    const radios = within(group).getAllByRole("radio");
    for (const r of radios) expect(r).toBeDisabled();
    // Den inaktiverade hjälptexten nämner BÅDA kanalerna som kan öppna väljaren.
    expect(
      screen.getByText(
        /Slå på matchningsnotiser eller notiser om företag du följer/
      )
    ).toBeInTheDocument();
  });

  it("pre-fill ON + Daily: toggeln är på, kadens-väljaren aktiv med Dagligen vald", () => {
    renderCard({ initialEnabled: true, initialCadence: "Daily" });
    expect(screen.getByRole("switch", { name: TOGGLE })).toHaveAttribute(
      "aria-checked",
      "true"
    );

    const group = screen.getByRole("radiogroup", { name: CADENCE_GROUP });
    const daily = within(group).getByRole("radio", { name: "Dagligen" });
    const weekly = within(group).getByRole("radio", { name: "Veckovis" });
    expect(daily).not.toBeDisabled();
    expect(daily).toHaveAttribute("aria-checked", "true");
    expect(weekly).toHaveAttribute("aria-checked", "false");
    // Aktiv hjälptext (inte den inaktiverade varianten).
    expect(
      screen.getByText(/Gäller e-postsammanfattningen av starka matchningar/)
    ).toBeInTheDocument();
  });

  it("ärlig framing: introt namnger e-post som leveranskanal (TD-116)", () => {
    renderCard();
    const intro = screen.getByText(/matchar vi nya annonser mot din profil/);
    // PR-4b skickar riktiga notiser via e-post (Topp direkt, Stark som sammanfattning);
    // copy:n MÅSTE namnge e-post (GDPR Art. 7(2) transparens, TD-116). Bra matchningar
    // ligger kvar i matchningslistan utan e-post.
    expect(intro).toHaveTextContent(/e-post/);
    expect(intro).toHaveTextContent(/matchningslista/);
  });

  it("kadens-hjälptexten namnger BÅDA utskicken den styr (F3 gjorde den delad)", () => {
    renderCard({ initialEnabled: true });
    expect(
      screen.getByText(/notiserna om företag du följer/)
    ).toBeInTheDocument();
  });
});

describe("BackgroundMatchCard — den delade kadensen (bevakning F4)", () => {
  it("följ-notiser PÅ men matchning AV: kadens-väljaren är ÅTKOMLIG", () => {
    // Kadensen driver båda utskicken. Utan detta kunde en användare som bara
    // slagit på följ-notiser inte välja takten för kanalen hon just slog på.
    renderCard({ initialEnabled: false, followEnabled: true });

    const group = screen.getByRole("radiogroup", { name: CADENCE_GROUP });
    for (const r of within(group).getAllByRole("radio")) {
      expect(r).not.toBeDisabled();
    }
    expect(
      screen.getByText(/Gäller e-postsammanfattningen av starka matchningar/)
    ).toBeInTheDocument();
  });

  it("kadens-byte med matchning AV skickar {enabled:false} — kadensen skrivs, samtycket rörs inte", async () => {
    const user = userEvent.setup();
    renderCard({
      initialEnabled: false,
      initialCadence: "Weekly",
      followEnabled: true,
    });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    await waitFor(() => expect(consentMock).toHaveBeenCalledTimes(1));
    // `enabled: false` är sanningen om DENNA kanal och är GDPR-säkert: domänen
    // stämplar en Art. 7(3)-withdrawal endast vid övergången på->av.
    expect(consentMock).toHaveBeenCalledWith({
      enabled: false,
      cadence: "Daily",
    });
  });
});

describe("BackgroundMatchCard — opt-in/opt-out save", () => {
  it("slå på toggeln skickar {enabled:true, cadence:Weekly} och aktiverar väljaren", async () => {
    const user = userEvent.setup();
    renderCard(); // default OFF + Weekly

    await user.click(screen.getByRole("switch", { name: TOGGLE }));

    await waitFor(() => expect(consentMock).toHaveBeenCalledTimes(1));
    expect(consentMock).toHaveBeenCalledWith({
      enabled: true,
      cadence: "Weekly",
    });
    // Kadens-väljaren blir aktiv direkt (optimistiskt).
    const group = screen.getByRole("radiogroup", { name: CADENCE_GROUP });
    expect(
      within(group).getByRole("radio", { name: "Veckovis" })
    ).not.toBeDisabled();
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

    await user.click(screen.getByRole("switch", { name: TOGGLE }));

    await waitFor(() => expect(consentMock).toHaveBeenCalledTimes(1));
    expect(consentMock).toHaveBeenCalledWith({
      enabled: false,
      cadence: "Daily",
    });
    // Väljaren blir inaktiverad igen efter opt-out (följ-kanalen är också av).
    const group = screen.getByRole("radiogroup", { name: CADENCE_GROUP });
    for (const r of within(group).getAllByRole("radio")) {
      expect(r).toBeDisabled();
    }
  });

  it("lyckad save visar status-raden 'Sparat HH:mm'", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(screen.getByRole("switch", { name: TOGGLE }));

    expect(await screen.findByText(/^Sparat \d{2}:\d{2}$/)).toBeInTheDocument();
  });

  it("misslyckad save återställer toggeln och visar role=alert", async () => {
    const user = userEvent.setup();
    consentMock.mockResolvedValue({ success: false, error: "nej" });
    renderCard(); // OFF

    await user.click(screen.getByRole("switch", { name: TOGGLE }));

    // Toggeln reverteras till av + alert syns.
    await waitFor(() =>
      expect(screen.getByRole("switch", { name: TOGGLE })).toHaveAttribute(
        "aria-checked",
        "false"
      )
    );
    expect(screen.getByRole("alert")).toHaveTextContent("nej");
  });

  it("misslyckad kadens-save återställer kadensen till föregående värde", async () => {
    const user = userEvent.setup();
    consentMock.mockResolvedValue({ success: false, error: "nej" });
    renderCard({ initialEnabled: true, initialCadence: "Weekly" });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    // Reverten går genom SettingsForm-ägaren (onCadenceChange) — Veckovis igen.
    await waitFor(() =>
      expect(screen.getByRole("radio", { name: "Veckovis" })).toHaveAttribute(
        "aria-checked",
        "true"
      )
    );
    expect(screen.getByRole("alert")).toHaveTextContent("nej");
  });
});
