import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import InterceptedCvNyModal from "./page";

const redirect = vi.fn();
const getServerSession = vi.fn();

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => redirect(url),
  useRouter: () => ({ push: vi.fn(), back: vi.fn() }),
}));

// CreateResumeForm anropar createResumeAction via useActionState; mocka
// server-actionen till en no-op så formuläret monterar i jsdom.
vi.mock("@/lib/actions/resumes", () => ({
  createResumeAction: vi.fn(),
}));

async function renderModal() {
  const element = await InterceptedCvNyModal();
  return render(element);
}

describe("@modal/(.)cv/ny intercepting route", () => {
  beforeEach(() => {
    redirect.mockReset();
    getServerSession.mockReset();
  });

  it("redirectar till /logga-in när användaren saknar session", async () => {
    getServerSession.mockResolvedValue(null);
    await renderModal();
    expect(redirect).toHaveBeenCalledWith("/logga-in");
  });

  it("renderar RouteModalShell med titel 'Nytt CV' + CreateResumeForm för inloggad", async () => {
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
    await renderModal();

    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");

    const labelledby = dialog.getAttribute("aria-labelledby");
    expect(document.getElementById(labelledby!)).toHaveTextContent("Nytt CV");

    // CreateResumeForm-närvaro: fälten + primär-knappen.
    expect(screen.getByLabelText("Namn på CV")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Skapa CV" })
    ).toBeInTheDocument();
  });
});
