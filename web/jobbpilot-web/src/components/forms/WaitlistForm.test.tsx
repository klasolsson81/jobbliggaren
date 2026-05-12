import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { WaitlistForm } from "./WaitlistForm";

type WaitlistActionState =
  | { status: "idle" }
  | { status: "success"; email: string }
  | { status: "error"; error: string };

const requestWaitlistMock =
  vi.fn<
    (
      prevState: WaitlistActionState,
      formData: FormData,
    ) => Promise<WaitlistActionState>
  >();

vi.mock("@/lib/waitlist/actions", () => ({
  requestWaitlistAction: (
    prevState: WaitlistActionState,
    formData: FormData,
  ) => requestWaitlistMock(prevState, formData),
}));

describe("WaitlistForm", () => {
  beforeEach(() => {
    requestWaitlistMock.mockReset();
    requestWaitlistMock.mockResolvedValue({ status: "idle" });
  });

  it("renders email-fält + submit-knapp", () => {
    render(<WaitlistForm />);
    expect(screen.getByLabelText("E-postadress")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    ).toBeInTheDocument();
  });

  it("submits with entered email", async () => {
    const user = userEvent.setup();
    render(<WaitlistForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.click(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    );

    expect(requestWaitlistMock).toHaveBeenCalledTimes(1);
    const call = requestWaitlistMock.mock.calls[0];
    if (!call) throw new Error("requestWaitlistAction was not invoked");
    const formData = call[1];
    expect(formData).toBeInstanceOf(FormData);
    expect(formData.get("email")).toBe("anna@example.se");
  });

  it("visar success-bekräftelse med email efter lyckad signup", async () => {
    requestWaitlistMock.mockResolvedValueOnce({
      status: "success",
      email: "anna@example.se",
    });

    const user = userEvent.setup();
    render(<WaitlistForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.click(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    );

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Anmälan registrerad.");
    expect(status).toHaveTextContent("anna@example.se");
  });

  it("visar server-fel som role=alert vid error-state", async () => {
    requestWaitlistMock.mockResolvedValueOnce({
      status: "error",
      error: "Registreringar är just nu stängda.",
    });

    const user = userEvent.setup();
    render(<WaitlistForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.click(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    );

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Registreringar är just nu stängda.");
  });

  it("markerar email som required (HTML-attribut)", () => {
    render(<WaitlistForm />);
    expect(screen.getByLabelText("E-postadress")).toBeRequired();
  });
});
