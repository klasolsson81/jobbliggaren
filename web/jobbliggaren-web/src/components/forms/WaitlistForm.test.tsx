import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { WaitlistForm } from "./WaitlistForm";

type WaitlistActionState =
  | { status: "idle" }
  | { status: "success"; email: string }
  | { status: "error"; error: string; fieldErrors?: Record<string, string> };

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

const VALID_NAME = "Anna Testperson";
const VALID_EMAIL = "anna@example.se";
const VALID_MOTIVATION = "Jag vill testa Jobbliggaren för att hantera ansökningar.";

async function fillRequiredFields(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("Namn"), VALID_NAME);
  await user.type(screen.getByLabelText("E-postadress"), VALID_EMAIL);
  await user.type(
    screen.getByLabelText(/Varför vill du använda Jobbliggaren/i),
    VALID_MOTIVATION,
  );
}

describe("WaitlistForm", () => {
  beforeEach(() => {
    requestWaitlistMock.mockReset();
    requestWaitlistMock.mockResolvedValue({ status: "idle" });
  });

  it("renderar fält + disclaimer-länkar + submit-knapp", () => {
    render(<WaitlistForm />);
    expect(screen.getByLabelText("Namn")).toBeInTheDocument();
    expect(screen.getByLabelText("E-postadress")).toBeInTheDocument();
    expect(
      screen.getByLabelText(/Varför vill du använda Jobbliggaren/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: /e-post med information/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: /användarvillkor/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: /nödvändiga cookies/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    ).toBeInTheDocument();
  });

  it("har INGA obligatoriska policies/cookies-checkboxar (GDPR Art. 6(1)(b))", () => {
    render(<WaitlistForm />);
    expect(
      screen.queryByRole("checkbox", { name: /användarvillkor/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("checkbox", { name: /godkänner cookies/i }),
    ).not.toBeInTheDocument();
  });

  it("submitar med ifyllda fält och rätt FormData", async () => {
    requestWaitlistMock.mockResolvedValueOnce({ status: "idle" });
    const user = userEvent.setup();
    render(<WaitlistForm />);

    await fillRequiredFields(user);
    await user.click(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    );

    await waitFor(() => {
      expect(requestWaitlistMock).toHaveBeenCalled();
    });

    const call = requestWaitlistMock.mock.calls[0];
    if (!call) throw new Error("requestWaitlistAction was not invoked");
    const formData = call[1];
    expect(formData).toBeInstanceOf(FormData);
    expect(formData.get("name")).toBe(VALID_NAME);
    expect(formData.get("email")).toBe(VALID_EMAIL);
    expect(formData.get("motivation")).toBe(VALID_MOTIVATION);
    expect(formData.get("marketingEmailAccepted")).toBe("false");
    // Inga policies/cookies-fält ska skickas — de hör inte längre till payload.
    expect(formData.get("policiesAccepted")).toBeNull();
    expect(formData.get("cookiesAccepted")).toBeNull();
  });

  it("visar success-bekräftelse med email efter lyckad signup", async () => {
    requestWaitlistMock.mockResolvedValueOnce({
      status: "success",
      email: VALID_EMAIL,
    });
    const user = userEvent.setup();
    render(<WaitlistForm />);

    await fillRequiredFields(user);
    await user.click(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    );

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Tack för din anmälan.");
    expect(status).toHaveTextContent(VALID_EMAIL);
  });

  it("visar server-fel som role=alert vid generic error-state", async () => {
    requestWaitlistMock.mockResolvedValueOnce({
      status: "error",
      error: "Anmälningar är just nu stängda. Försök igen senare när vi öppnar nästa pulse.",
    });
    const user = userEvent.setup();
    render(<WaitlistForm />);

    await fillRequiredFields(user);
    await user.click(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    );

    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toMatch(/stängda/i);
  });

  it("marketing-samtycket är default false — submit lyckas utan att markera det", async () => {
    requestWaitlistMock.mockResolvedValueOnce({
      status: "success",
      email: VALID_EMAIL,
    });
    const user = userEvent.setup();
    render(<WaitlistForm />);

    await fillRequiredFields(user);
    await user.click(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    );

    await waitFor(() => {
      expect(requestWaitlistMock).toHaveBeenCalled();
    });
    const call = requestWaitlistMock.mock.calls[0];
    if (!call) throw new Error("requestWaitlistAction was not invoked");
    expect(call[1].get("marketingEmailAccepted")).toBe("false");
  });

  it("submitar med marketing-samtycke när användare klickar i checkboxen", async () => {
    requestWaitlistMock.mockResolvedValueOnce({
      status: "success",
      email: VALID_EMAIL,
    });
    const user = userEvent.setup();
    render(<WaitlistForm />);

    await fillRequiredFields(user);
    await user.click(
      screen.getByRole("checkbox", { name: /e-post med information/i }),
    );
    await user.click(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    );

    await waitFor(() => {
      expect(requestWaitlistMock).toHaveBeenCalled();
    });
    const call = requestWaitlistMock.mock.calls[0];
    if (!call) throw new Error("requestWaitlistAction was not invoked");
    expect(call[1].get("marketingEmailAccepted")).toBe("true");
  });

  it("blockerar submit vid för kort motivering", async () => {
    const user = userEvent.setup();
    render(<WaitlistForm />);

    await user.type(screen.getByLabelText("Namn"), VALID_NAME);
    await user.type(screen.getByLabelText("E-postadress"), VALID_EMAIL);
    await user.type(
      screen.getByLabelText(/Varför vill du använda Jobbliggaren/i),
      "kort",
    );
    await user.click(
      screen.getByRole("button", { name: "Anmäl till väntelista" }),
    );

    expect(requestWaitlistMock).not.toHaveBeenCalled();
    await waitFor(() => {
      const alerts = screen.queryAllByRole("alert");
      expect(
        alerts.some((a) => /minst 10 tecken/i.test(a.textContent ?? "")),
      ).toBe(true);
    });
  });
});
