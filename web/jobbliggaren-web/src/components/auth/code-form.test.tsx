import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { CodeStepState } from "@/lib/auth/challenge-action-state";
import { CodeForm } from "./code-form";

const verifyCodeMock =
  vi.fn<(prev: CodeStepState, formData: FormData) => Promise<CodeStepState>>();

vi.mock("@/lib/auth/challenge-actions", () => ({
  verifyCode: (prev: CodeStepState, formData: FormData) => verifyCodeMock(prev, formData),
}));

const WRONG = "Koden stämmer inte. Kontrollera siffrorna och försök igen.";

describe("CodeForm", () => {
  beforeEach(() => {
    verifyCodeMock.mockReset();
    verifyCodeMock.mockResolvedValue(null);
  });

  it("is ONE one-time-code input over the six boxes", () => {
    render(<CodeForm />);

    expect(screen.getAllByRole("textbox")).toHaveLength(1);
    const field = screen.getByLabelText("Sexsiffrig kod");
    expect(field).toHaveAttribute("autocomplete", "one-time-code");
    expect(field).toHaveAttribute("inputmode", "numeric");
    expect(field).toHaveAttribute("maxlength", "6");
    expect(field).toHaveAttribute("pattern", "^\\d+$");
    expect(field).not.toHaveAttribute("placeholder");
    expect(field).toHaveAccessibleDescription(
      "Kommer inget mejl inom några minuter kan du skicka en ny kod, eller byta e-postadress."
    );
  });

  it("names what the press does on both paths, and says how long the login lasts right above it", () => {
    const { container } = render(<CodeForm />);

    // Not "Logga in": a new address goes on to the consent step, with no session yet.
    const primary = screen.getByRole("button", { name: "Bekräfta koden" });
    const disclosure = screen.getByText(/Du förblir inloggad på den här enheten i upp till 180 dagar/);
    expect(disclosure.compareDocumentPosition(primary) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(container.querySelector("form")?.lastElementChild).toBe(primary);
  });

  it("posts the typed code", async () => {
    const user = userEvent.setup();
    render(<CodeForm />);

    await user.type(screen.getByLabelText("Sexsiffrig kod"), "123456");
    await user.click(screen.getByRole("button", { name: "Bekräfta koden" }));

    await waitFor(() => expect(verifyCodeMock).toHaveBeenCalledTimes(1));
    expect(verifyCodeMock.mock.lastCall![1].get("code")).toBe("123456");
  });

  it("answers a wrong code as an alert on the field, keeps the field, and moves focus to it", async () => {
    verifyCodeMock.mockResolvedValue({ error: WRONG, channel: "field" });
    const user = userEvent.setup();
    render(<CodeForm />);

    await user.type(screen.getByLabelText("Sexsiffrig kod"), "000000");
    await user.click(screen.getByRole("button", { name: "Bekräfta koden" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(WRONG);
    expect(alert).not.toHaveTextContent("Ett försök kvar");
    const field = screen.getByLabelText("Sexsiffrig kod");
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field.getAttribute("aria-describedby")).toContain(alert.id);
    await waitFor(() => expect(field).toHaveFocus());
  });

  it("empties the field after a miss, boxes included, so the code is typed again", async () => {
    verifyCodeMock.mockResolvedValue({ error: WRONG, channel: "field" });
    const user = userEvent.setup();
    const { container } = render(<CodeForm />);

    await user.type(screen.getByLabelText("Sexsiffrig kod"), "000000");
    await user.click(screen.getByRole("button", { name: "Bekräfta koden" }));
    await screen.findByRole("alert");

    await waitFor(() => expect(screen.getByLabelText("Sexsiffrig kod")).toHaveValue(""));
    const boxes = container.querySelectorAll('[data-slot="input-otp-slot"]');
    expect(Array.from(boxes).map((box) => box.textContent).join("")).toBe("");
  });

  it("warns before the last attempt, in the same alert so it is announced once", async () => {
    verifyCodeMock.mockResolvedValue({ error: WRONG, channel: "field", lastAttempt: true });
    const user = userEvent.setup();
    render(<CodeForm />);

    await user.type(screen.getByLabelText("Sexsiffrig kod"), "000000");
    await user.click(screen.getByRole("button", { name: "Bekräfta koden" }));

    expect(await screen.findAllByRole("alert")).toHaveLength(1);
    expect(screen.getByRole("alert")).toHaveTextContent(
      `${WRONG} Ett försök kvar. Sedan behöver du begära en ny kod.`
    );
  });

  it("answers an outage as a status and sends focus to the MESSAGE: there is nothing to correct", async () => {
    verifyCodeMock.mockResolvedValue({
      error: "Det går inte att logga in just nu. Försök igen om några minuter.",
      channel: "status",
    });
    const user = userEvent.setup();
    render(<CodeForm />);

    await user.type(screen.getByLabelText("Sexsiffrig kod"), "123456");
    await user.click(screen.getByRole("button", { name: "Bekräfta koden" }));

    const status = await screen.findByRole("status");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Sexsiffrig kod")).not.toHaveAttribute("aria-invalid");
    await waitFor(() => expect(status).toHaveFocus());
  });
});
