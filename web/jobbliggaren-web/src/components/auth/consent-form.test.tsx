import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ConsentStepState } from "@/lib/auth/challenge-action-state";
import { ConsentForm } from "./consent-form";

const completeRegistrationMock =
  vi.fn<(prev: ConsentStepState, formData: FormData) => Promise<ConsentStepState>>();

vi.mock("@/lib/auth/challenge-actions", () => ({
  completeRegistration: (prev: ConsentStepState, formData: FormData) =>
    completeRegistrationMock(prev, formData),
}));

// The checkbox's accessible name. The privacy policy is NOT in it: the box accepts the terms, and
// the policy is a notice the user is pointed to (ADR 0142 D6).
const TERMS = "Jag godkänner användarvillkoren.";

describe("ConsentForm", () => {
  beforeEach(() => {
    completeRegistrationMock.mockReset();
    completeRegistrationMock.mockResolvedValue(null);
  });

  it("asks for the terms alone, unticked, with the privacy policy in a sibling sentence", () => {
    render(<ConsentForm />);

    const box = screen.getByRole("checkbox", { name: TERMS });
    expect(box).not.toBeChecked();
    expect(box).toBeRequired();
    expect(screen.getByRole("link", { name: "användarvillkoren" })).toHaveAttribute("href", "/villkor");

    const sibling = screen.getByText(/Vi behandlar dina uppgifter enligt/);
    expect(sibling.closest("label")).toBeNull();
    expect(screen.getByRole("link", { name: "integritetspolicyn" })).toHaveAttribute(
      "href",
      "/integritet"
    );
  });

  it("shows no address: the account is created on what the grant proved, not on what a cookie held", () => {
    const { container } = render(<ConsentForm />);

    expect(container.textContent).not.toMatch(/@/);
  });

  it("puts the persistence disclosure directly above the one primary, and offers a way out", () => {
    const { container } = render(<ConsentForm />);

    const primary = screen.getByRole("button", { name: "Skapa konto" });
    expect(container.querySelector("form")?.lastElementChild).toBe(primary);
    expect(primary.previousElementSibling).toHaveTextContent(
      /Du förblir inloggad på den här enheten i upp till 180 dagar/
    );
    expect(
      screen.getByRole("link", { name: "Börja om med en annan e-postadress" })
    ).toHaveAttribute("href", "/logga-in");
  });

  it("posts the acceptance and no grant: the grant comes from the cookie, server-side", async () => {
    const user = userEvent.setup();
    render(<ConsentForm />);

    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    await waitFor(() => expect(completeRegistrationMock).toHaveBeenCalledTimes(1));
    const posted = completeRegistrationMock.mock.lastCall![1];
    expect(posted.get("acceptTerms")).toBe("on");
    expect([...posted.keys()]).toEqual(["acceptTerms"]);
  });

  it("lets the ACTION refuse an unticked box, as an alert on the checkbox with focus moved to it", async () => {
    completeRegistrationMock.mockResolvedValue({
      error: "Du behöver godkänna användarvillkoren för att skapa kontot.",
      channel: "field",
    });
    const user = userEvent.setup();
    render(<ConsentForm />);

    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Du behöver godkänna användarvillkoren för att skapa kontot.");
    const box = screen.getByRole("checkbox", { name: TERMS });
    expect(box).toHaveAttribute("aria-invalid", "true");
    expect(box.getAttribute("aria-describedby")).toContain(alert.id);
    await waitFor(() => expect(box).toHaveFocus());
  });
});
