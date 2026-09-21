import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ConsentStepState } from "@/lib/auth/challenge-action-state";
import { ConsentForm } from "./consent-form";

const completeRegistrationMock =
  vi.fn<(prev: ConsentStepState, formData: FormData) => Promise<ConsentStepState>>();
const changeEmailMock = vi.fn<() => Promise<void>>();

vi.mock("@/lib/auth/challenge-actions", () => ({
  completeRegistration: (prev: ConsentStepState, formData: FormData) =>
    completeRegistrationMock(prev, formData),
  changeEmail: () => changeEmailMock(),
}));

// The checkbox's accessible name. The privacy policy is NOT in it: the box accepts the terms, and
// the policy is a notice the user is pointed to (ADR 0142 D6).
const TERMS = "Jag godkänner användarvillkoren.";

describe("ConsentForm", () => {
  beforeEach(() => {
    completeRegistrationMock.mockReset();
    completeRegistrationMock.mockResolvedValue(null);
    changeEmailMock.mockReset();
    changeEmailMock.mockResolvedValue(undefined);
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

  it("puts the persistence disclosure directly above the one primary", () => {
    const { container } = render(<ConsentForm />);

    const primary = screen.getByRole("button", { name: "Skapa konto" });
    expect(container.querySelector("form")?.lastElementChild).toBe(primary);
    expect(primary.previousElementSibling).toHaveTextContent(
      /Du förblir inloggad på den här enheten i upp till 180 dagar/
    );
  });

  it("walks away through the action that clears the grant: a link would leave it on the device", async () => {
    const user = userEvent.setup();
    const { container } = render(<ConsentForm />);

    expect(screen.queryByRole("link", { name: "Börja om med en annan e-postadress" })).toBeNull();
    const exit = screen.getByRole("button", { name: "Börja om med en annan e-postadress" });
    // A form of its own, never nested in the consent form.
    expect(container.querySelectorAll("form form")).toHaveLength(0);

    await user.click(exit);

    await waitFor(() => expect(changeEmailMock).toHaveBeenCalledTimes(1));
    expect(completeRegistrationMock).not.toHaveBeenCalled();
  });

  it("answers an outage as a status, and never marks the checkbox as wrong", async () => {
    completeRegistrationMock.mockResolvedValue({
      error: "Tjänsten svarar inte just nu. Försök igen om en stund.",
      channel: "status",
    });
    const user = userEvent.setup();
    render(<ConsentForm />);
    const box = screen.getByRole("checkbox", { name: TERMS });
    const describedByAtRest = box.getAttribute("aria-describedby");

    await user.click(box);
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const message = await screen.findByText("Tjänsten svarar inte just nu. Försök igen om en stund.");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(box).not.toHaveAttribute("aria-invalid");
    // The status paragraph carries no id, so an id added here would point at nothing.
    expect(box.getAttribute("aria-describedby")).toBe(describedByAtRest);
    await waitFor(() => expect(message).toHaveFocus());
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
