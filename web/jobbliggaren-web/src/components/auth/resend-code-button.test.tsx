import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ResendState } from "@/lib/auth/challenge-action-state";
import { ResendCodeButton } from "./resend-code-button";

const resendCodeMock = vi.fn<() => Promise<ResendState>>();

vi.mock("@/lib/auth/challenge-actions", () => ({
  resendCode: () => resendCodeMock(),
}));

const SENT_AT = 1_800_000_000;
const RECEIPT =
  "Vi har tagit emot din begäran. Kontrollera inkorgen och skräpposten. Skriv in koden från det senaste mejlet.";

describe("ResendCodeButton", () => {
  beforeEach(() => {
    resendCodeMock.mockReset();
    resendCodeMock.mockResolvedValue({ status: "sent" });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("STARTS in cooldown: a press right after the first mail would kill the code already sent", () => {
    render(<ResendCodeButton sentAt={SENT_AT} initialCooldownSeconds={60} primary={false} />);

    expect(screen.getByRole("button", { name: "Skicka ny kod" })).toBeDisabled();
    expect(screen.getByText("Du kan skicka en ny kod om 60 sekunder.")).toBeInTheDocument();
  });

  it("says what a press costs, before the press and outside the live region", () => {
    render(<ResendCodeButton sentAt={SENT_AT} initialCooldownSeconds={0} primary={false} />);

    const consequence = screen.getByText(
      "En ny kod ersätter den förra. Skriv in koden från det senaste mejlet."
    );
    expect(screen.getByRole("status")).not.toContainElement(consequence);
  });

  it("keeps the countdown OUT of the live region, so a screen reader is not read a number a second", () => {
    render(<ResendCodeButton sentAt={SENT_AT} initialCooldownSeconds={42} primary={false} />);

    const countdown = screen.getByText("Du kan skicka en ny kod om 42 sekunder.");
    expect(screen.getByRole("status")).not.toContainElement(countdown);
  });

  it("counts down and opens at zero", () => {
    vi.useFakeTimers();
    render(<ResendCodeButton sentAt={SENT_AT} initialCooldownSeconds={2} primary={false} />);

    act(() => vi.advanceTimersByTime(1000));
    expect(screen.getByText("Du kan skicka en ny kod om 1 sekund.")).toBeInTheDocument();

    act(() => vi.advanceTimersByTime(1000));
    expect(screen.queryByText(/Du kan skicka en ny kod om/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Skicka ny kod" })).toBeEnabled();
  });

  it("shows the receipt in the status region, and the receipt claims no mail was sent", async () => {
    const user = userEvent.setup();
    render(<ResendCodeButton sentAt={SENT_AT} initialCooldownSeconds={0} primary={false} />);

    await user.click(screen.getByRole("button", { name: "Skicka ny kod" }));

    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent(RECEIPT));
    // Two branches write a challenge and send nothing, so the page never says "we have sent".
    expect(screen.getByRole("status")).not.toHaveTextContent(/skickat/i);
  });

  it("counts again when the page hands it a NEW challenge, and keeps its receipt across that re-render", async () => {
    const user = userEvent.setup();
    const { rerender } = render(
      <ResendCodeButton sentAt={SENT_AT} initialCooldownSeconds={0} primary={true} />
    );
    await user.click(screen.getByRole("button", { name: "Skicka ny kod" }));
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent(RECEIPT));

    // What the page does after `resendCode` rewrote the cookie: same position, new challenge.
    rerender(<ResendCodeButton sentAt={SENT_AT + 300} initialCooldownSeconds={60} primary={false} />);

    expect(screen.getByRole("button", { name: "Skicka ny kod" })).toBeDisabled();
    expect(screen.getByText("Du kan skicka en ny kod om 60 sekunder.")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(RECEIPT);
  });

  it("answers a throttle as plain status text, never in danger colour", async () => {
    resendCodeMock.mockResolvedValue({
      status: "error",
      error: "För många försök. Vänta en stund och försök igen.",
    });
    const user = userEvent.setup();
    render(<ResendCodeButton sentAt={SENT_AT} initialCooldownSeconds={0} primary={false} />);

    await user.click(screen.getByRole("button", { name: "Skicka ny kod" }));

    const message = await screen.findByText("För många försök. Vänta en stund och försök igen.");
    expect(message.className).not.toMatch(/danger/);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("is the primary once the code is dead, and an outline button beside a live field", () => {
    const { rerender } = render(
      <ResendCodeButton sentAt={SENT_AT} initialCooldownSeconds={0} primary={true} />
    );
    expect(screen.getByRole("button", { name: "Skicka ny kod" })).toHaveAttribute(
      "data-variant",
      "default"
    );

    rerender(<ResendCodeButton sentAt={SENT_AT} initialCooldownSeconds={0} primary={false} />);
    expect(screen.getByRole("button", { name: "Skicka ny kod" })).toHaveAttribute(
      "data-variant",
      "outline"
    );
  });
});
