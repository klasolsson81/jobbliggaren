import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";

// Server-actions körs aldrig i jsdom — mocka den action kortet importerar.
const { followConsentMock } = vi.hoisted(() => ({
  followConsentMock: vi.fn(),
}));
vi.mock("@/lib/actions/me", () => ({
  updateFollowedCompanyNotificationConsentAction: followConsentMock,
}));

import { FollowedCompanyNotificationsCard } from "./followed-company-notifications-card";
import type { DigestCadence } from "@/lib/dto/me";

/**
 * `enabled` är KONTROLLERAT av SettingsForm (matchnings-kortet läser samma
 * värde för att veta om kadens-väljaren ska vara åtkomlig). Hosten speglar den
 * ägaren så optimistisk uppdatering OCH revert-vid-fel testas som de körs.
 */
function TestHost({
  initialEnabled = false,
  cadence = "Weekly",
}: {
  initialEnabled?: boolean;
  cadence?: DigestCadence;
}) {
  const [enabled, setEnabled] = useState(initialEnabled);
  return (
    <FollowedCompanyNotificationsCard
      enabled={enabled}
      onEnabledChange={setEnabled}
      cadence={cadence}
    />
  );
}

function renderCard(overrides?: React.ComponentProps<typeof TestHost>) {
  return render(<TestHost {...overrides} />);
}

const TOGGLE = "Mejla mig nya annonser från företag jag följer";

beforeEach(() => {
  followConsentMock.mockReset();
  followConsentMock.mockResolvedValue({ success: true });
});

describe("FollowedCompanyNotificationsCard — samtyckets ram (GDPR)", () => {
  it("default OFF (Art. 6(1)(a) opt-in)", () => {
    renderCard();
    expect(screen.getByRole("switch", { name: TOGGLE })).toHaveAttribute(
      "aria-checked",
      "false"
    );
  });

  it("introt säger att in-app-notiserna går OAVSETT flaggan (7C, Art. 7(2))", () => {
    // Utan den meningen tror användaren att avstängt = inga notiser alls — och
    // att flaggan krävs för Översikts-räknaren. Det är F4:s mörk-räls-mitigering
    // i copy-form; kortet får inte skeppas utan den.
    renderCard();
    const intro = screen.getByText(/visas alltid i appen/);
    expect(intro).toHaveTextContent(/oavsett vad du väljer här/);
    // Namnger den yta som faktiskt visar något i dag (Översikts-räknaren) ...
    expect(intro).toHaveTextContent(/Översikt/);
    // ... och e-post som den kanal flaggan styr.
    expect(intro).toHaveTextContent(/mejla/);
  });

  it("toggle-beskrivningen bär Art. 7(3)-withdrawal-meningen", () => {
    renderCard();
    expect(
      screen.getByText(/dra tillbaka samtycket när som helst/)
    ).toBeInTheDocument();
  });

  it("kortet renderar INGEN kadens-kontroll (takten är delad, ADR 0087 D2)", () => {
    renderCard({ initialEnabled: true });
    // Två kontroller för ett värde vore garanterad drift — kadensen visas som
    // text och ändras i matchnings-kortet.
    expect(screen.queryByRole("radiogroup")).not.toBeInTheDocument();
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
  });
});

describe("FollowedCompanyNotificationsCard — den delade takten som text", () => {
  it("påslaget: visar gällande takt med den befintliga etiketten och pekar på kortet där den ändras", () => {
    renderCard({ initialEnabled: true, cadence: "Daily" });
    const note = screen.getByText(/Utskicket följer samma takt/);
    expect(note).toHaveTextContent(/Dagligen/);
    expect(note).toHaveTextContent(/kortet Matchningsnotiser/);
  });

  it("avslaget: takt-noten är generisk (ingen takt att utlova)", () => {
    renderCard({ initialEnabled: false, cadence: "Daily" });
    const note = screen.getByText(/Utskicket följer samma takt/);
    expect(note).not.toHaveTextContent(/Dagligen/);
  });

  it("lovar INGEN filter-affordans (den finns inte förrän F4b)", () => {
    // Copy som pekar på en kontroll som inte finns är ett löfte vi bryter i
    // samma andetag. Filter-noten skeppas med filter-UI:t, inte före det.
    renderCard({ initialEnabled: true });
    expect(screen.queryByText(/filter/i)).not.toBeInTheDocument();
  });
});

describe("FollowedCompanyNotificationsCard — save", () => {
  it("opt-in skickar {enabled:true} — ingen kadens i bodyn", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(screen.getByRole("switch", { name: TOGGLE }));

    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(1));
    // Kadensen är delad och skrivs av det andra kortet — att skicka den här
    // skulle implicera en andra, oberoende takt som inte finns.
    expect(followConsentMock).toHaveBeenCalledWith({ enabled: true });
  });

  it("toggeln flippar OPTIMISTISKT innan servern svarat (annars är den död)", async () => {
    // Utan det optimistiska steget skulle switchen bara röra sig vid revert:
    // en död samtyckes-toggle som ändå passerar varje annat test i filen (de
    // asserterar mocken eller status-raden, inte switchens läge under sparet).
    // Därför hålls actionen svävande här tills läget är avläst.
    const user = userEvent.setup();
    let settle: (value: { success: true }) => void = () => {};
    followConsentMock.mockReturnValue(
      new Promise<{ success: true }>((resolve) => {
        settle = resolve;
      })
    );
    renderCard(); // OFF

    await user.click(screen.getByRole("switch", { name: TOGGLE }));

    expect(screen.getByRole("switch", { name: TOGGLE })).toHaveAttribute(
      "aria-checked",
      "true"
    );

    settle({ success: true });
    expect(await screen.findByText(/^Sparat \d{2}:\d{2}$/)).toBeInTheDocument();
  });

  it("opt-out (Art. 7(3)-withdrawal) skickar {enabled:false}", async () => {
    const user = userEvent.setup();
    renderCard({ initialEnabled: true });

    await user.click(screen.getByRole("switch", { name: TOGGLE }));

    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(1));
    expect(followConsentMock).toHaveBeenCalledWith({ enabled: false });
  });

  it("lyckad save visar status-raden 'Sparat HH:mm'", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(screen.getByRole("switch", { name: TOGGLE }));

    expect(await screen.findByText(/^Sparat \d{2}:\d{2}$/)).toBeInTheDocument();
  });

  it("misslyckad save återställer toggeln och visar role=alert", async () => {
    const user = userEvent.setup();
    followConsentMock.mockResolvedValue({ success: false, error: "nej" });
    renderCard();

    await user.click(screen.getByRole("switch", { name: TOGGLE }));

    await waitFor(() =>
      expect(screen.getByRole("switch", { name: TOGGLE })).toHaveAttribute(
        "aria-checked",
        "false"
      )
    );
    expect(screen.getByRole("alert")).toHaveTextContent("nej");
    // Fel och kvittens är ömsesidigt uteslutande live-regioner.
    expect(screen.queryByText(/^Sparat/)).not.toBeInTheDocument();
  });
});
