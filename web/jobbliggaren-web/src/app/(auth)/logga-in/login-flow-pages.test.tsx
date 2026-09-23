import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import type { LoginFlow } from "@/lib/auth/login-flow";
import svPages from "../../../../messages/sv/pages.json";

// The four pages of the login flow. Each is an async Server Component that renders from the flow
// cookie, so the cookie module is the seam: what it returns is what the page has to go on.

const NOW = 1_800_000_000;

const mocks = vi.hoisted(() => ({
  readLoginFlow: vi.fn(),
  getSessionId: vi.fn(),
  redirect: vi.fn((path: string) => {
    throw new Error(`REDIRECT:${path}`);
  }),
}));

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { [namespace]: svPages }, namespace }),
}));
vi.mock("next/navigation", () => ({ redirect: mocks.redirect }));
vi.mock("@/lib/auth/login-flow-cookie", () => ({
  readLoginFlow: mocks.readLoginFlow,
  nowEpochSeconds: () => NOW,
}));
vi.mock("@/lib/auth/session", () => ({ getSessionId: mocks.getSessionId }));
vi.mock("@/lib/auth/challenge-actions", () => ({
  requestCode: vi.fn(),
  verifyCode: vi.fn(),
  resendCode: vi.fn(),
  changeEmail: vi.fn(),
  completeRegistration: vi.fn(),
  consumeLink: vi.fn(),
}));

import LoggaInPage from "./page";
import LoggaInKodPage from "./kod/page";
import LoggaInLankPage from "./lank/page";
import LoggaInVillkorPage from "./villkor/page";

const code: Extract<LoginFlow, { phase: "code" }> = {
  phase: "code",
  challengeId: "challenge-1",
  email: "anna@example.com",
  next: "",
  sentAt: NOW - 20,
};
const consent: LoginFlow = { phase: "consent", grantToken: "grant-1", next: "" };
const closed: LoginFlow = { phase: "outcome", result: { outcome: "registrationClosed" } };

const redirectOf = async (page: () => Promise<unknown>): Promise<string> => {
  try {
    await page();
  } catch (error) {
    return (error as Error).message.replace("REDIRECT:", "");
  }
  throw new Error("the page rendered instead of redirecting");
};

beforeEach(() => {
  mocks.readLoginFlow.mockReset().mockResolvedValue(null);
  // `null`, never `undefined`: that is what the real `getSessionId` answers with no cookie. A
  // stub returning `undefined` once hid a page that treated every visitor as logged in.
  mocks.getSessionId.mockReset().mockResolvedValue(null);
  mocks.redirect.mockClear();
});

describe("/logga-in", () => {
  const page = (searchParams: Record<string, string | string[]> = {}) =>
    LoggaInPage({ searchParams: Promise.resolve(searchParams) });

  it("is the address first and the inactive providers last, under one h1", async () => {
    render(await page());

    expect(
      screen.getByRole("heading", { level: 1, name: "Logga in eller skapa konto" })
    ).toBeInTheDocument();
    expect(
      screen.getByText("Du loggar in med en kod som vi skickar till din e-postadress. Du behöver inget lösenord.")
    ).toBeInTheDocument();

    const field = screen.getByLabelText("E-postadress");
    const providers = screen.getByRole("heading", { level: 2, name: "Andra sätt att logga in" });
    expect(field.compareDocumentPosition(providers) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // Fortsätt is the screen's only solid primary; the provider rows are outline.
    expect(
      screen.getAllByRole("button").filter((b) => b.getAttribute("data-variant") === "default")
    ).toHaveLength(1);
  });

  it("has no password field, no remember-me box and no link to a second auth page", async () => {
    const { container } = render(await page());

    expect(container.querySelector('input[type="password"]')).toBeNull();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(container.querySelector('a[href="/registrera"]')).toBeNull();
    expect(container.querySelector('a[href="/glomt-losenord"]')).toBeNull();
  });

  it("carries next into the form, taking the first of a repeated parameter", async () => {
    const { container } = render(await page({ next: ["/cv", "/evil"] }));

    expect(container.querySelector('input[name="next"]')).toHaveValue("/cv");
  });

  // "Byt e-postadress" lands here. A page that resumed from the cookie would make it a loop.
  it.each<[string, LoginFlow]>([
    ["a live code", code],
    ["a consent phase", consent],
    ["an outcome", closed],
  ])("never resumes or redirects on %s: it renders the resting form", async (_label, flow) => {
    mocks.readLoginFlow.mockResolvedValue(flow);

    render(await page());

    expect(mocks.redirect).not.toHaveBeenCalled();
    expect(screen.getByLabelText("E-postadress")).toHaveValue("");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("shows a notice above the form, which stays live", async () => {
    mocks.readLoginFlow.mockResolvedValue({ phase: "notice", notice: "grantUnusable" });

    render(await page());

    const notice = screen.getByRole("status");
    expect(notice).toHaveTextContent("Registreringen slutfördes inte");
    const field = screen.getByLabelText("E-postadress");
    expect(notice.compareDocumentPosition(field) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

describe("/logga-in/kod", () => {
  it("rests on an instruction, never on a claim that a mail was sent or to whom", async () => {
    mocks.readLoginFlow.mockResolvedValue(code);

    render(await LoggaInKodPage());

    expect(screen.getByRole("heading", { level: 1, name: "Ange koden" })).toBeInTheDocument();
    const resting = screen.getByText(/Kontrollera inkorgen och skräpposten\./);
    expect(resting).not.toHaveTextContent(/skickat|anna@example\.com/);
  });

  it("shows the typed address as its own statement, away from the sentence about the mail", async () => {
    mocks.readLoginFlow.mockResolvedValue(code);

    render(await LoggaInKodPage());

    const typed = screen.getByText("Du angav anna@example.com.");
    const resting = screen.getByText(/Kontrollera inkorgen och skräpposten\./);
    expect(resting).not.toContainElement(typed);
    // After the field group: address, then resend, then change address, last.
    const order = [typed, screen.getByRole("button", { name: "Skicka ny kod" }), screen.getByRole("button", { name: "Byt e-postadress" })];
    expect(order[0]!.compareDocumentPosition(order[1]!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(order[1]!.compareDocumentPosition(order[2]!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("hands the resend button the seconds left of the cooldown, counted from the mint", async () => {
    mocks.readLoginFlow.mockResolvedValue(code);

    render(await LoggaInKodPage());

    expect(screen.getByRole("button", { name: "Skicka ny kod" })).toBeDisabled();
    expect(screen.getByText("Du kan skicka en ny kod om 40 sekunder.")).toBeInTheDocument();
  });

  it.each(["expired", "burned"] as const)(
    "replaces the FIELD for a %s code, keeps the step, and makes resend the primary",
    async (dead) => {
      mocks.readLoginFlow.mockResolvedValue({ ...code, sentAt: NOW - 300, dead });

      render(await LoggaInKodPage());

      expect(screen.getByRole("heading", { level: 1, name: "Ange koden" })).toBeInTheDocument();
      expect(screen.queryByLabelText("Sexsiffrig kod")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Bekräfta koden" })).not.toBeInTheDocument();
      const resend = screen.getByRole("button", { name: "Skicka ny kod" });
      expect(resend).toHaveAttribute("data-variant", "default");
      expect(resend).toBeEnabled();
      expect(screen.getByText("Du angav anna@example.com.")).toBeInTheDocument();
    }
  );

  it("replaces the whole form with the outcome panel, and shows no address", async () => {
    mocks.readLoginFlow.mockResolvedValue(closed);

    const { container } = render(await LoggaInKodPage());

    expect(screen.getByRole("status")).toHaveTextContent("Registreringen är inte öppen ännu.");
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(container.textContent).not.toMatch(/@/);
  });

  it.each<[string, LoginFlow | null, string]>([
    ["no cookie", null, "/logga-in"],
    ["a notice", { phase: "notice", notice: "codeExpired" }, "/logga-in"],
    ["a consent phase", consent, "/logga-in/villkor"],
  ])("sends %s where it belongs", async (_label, flow, target) => {
    mocks.readLoginFlow.mockResolvedValue(flow);

    expect(await redirectOf(LoggaInKodPage)).toBe(target);
  });
});

describe("/logga-in/villkor", () => {
  it("identifies the account without an address and asks for the terms", async () => {
    mocks.readLoginFlow.mockResolvedValue(consent);

    const { container } = render(await LoggaInVillkorPage());

    expect(screen.getByRole("heading", { level: 1, name: "Skapa ditt konto" })).toBeInTheDocument();
    expect(
      screen.getByText("Kontot skapas på den e-postadress du nyss bekräftade med koden.")
    ).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Jag godkänner användarvillkoren." })).toBeInTheDocument();
    expect(container.textContent).not.toMatch(/@/);
  });

  it("renders an outcome of complete in place of the form", async () => {
    mocks.readLoginFlow.mockResolvedValue(closed);

    render(await LoggaInVillkorPage());

    expect(screen.getByRole("status")).toHaveTextContent("Registreringen är inte öppen ännu.");
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it.each<[string, LoginFlow | null, string]>([
    ["no cookie", null, "/logga-in"],
    ["a code phase", code, "/logga-in/kod"],
  ])("sends %s where it belongs", async (_label, flow, target) => {
    mocks.readLoginFlow.mockResolvedValue(flow);

    expect(await redirectOf(LoggaInVillkorPage)).toBe(target);
  });
});

describe("/logga-in/lank", () => {
  const page = (searchParams: Record<string, string | string[]>) =>
    LoggaInLankPage({ searchParams: Promise.resolve(searchParams) });

  it("offers one press for a token, and never reads the flow cookie", async () => {
    const { container } = render(await page({ token: "link-token", next: "/cv", email: "x@y.z" }));

    expect(screen.getByRole("heading", { level: 1, name: "Logga in på Jobbliggaren" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Logga in" })).toBeInTheDocument();
    expect(container.querySelector('input[name="token"]')).toHaveValue("link-token");
    // `token` is the only parameter read: nothing else from the URL reaches the form.
    expect(container.querySelectorAll("input")).toHaveLength(1);
    expect(mocks.readLoginFlow).not.toHaveBeenCalled();
  });

  it("switches to the already-logged-in arm when this browser holds a session", async () => {
    mocks.getSessionId.mockResolvedValue("session-1");

    render(await page({ token: "link-token" }));

    expect(screen.getByRole("heading", { level: 1, name: "Du är redan inloggad" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Fortsätt och logga in" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Stanna kvar som inloggad" })).toHaveAttribute("href", "/oversikt");
  });

  it.each<[string, Record<string, string | string[]>]>([
    ["no token", {}],
    ["an empty token", { token: "  " }],
    ["a token longer than the backend admits", { token: "x".repeat(129) }],
  ])("says the one sentence for %s and offers no button", async (_label, searchParams) => {
    render(await page(searchParams));

    expect(
      screen.getByText("Länken går inte att använda. Begär en ny kod på inloggningssidan.")
    ).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("reads the first of a repeated token", async () => {
    const { container } = render(await page({ token: ["first", "second"] }));

    expect(container.querySelector('input[name="token"]')).toHaveValue("first");
  });
});
