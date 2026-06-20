import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import InterceptedCvImportModal from "./page";

const redirect = vi.fn();
const getServerSession = vi.fn();

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

// Modal-chromet (RouteModalShell) och CvUploadForm använder useRouter;
// redirect används av auth-grinden. Mockas så det async server-trädet kan
// pre-resolvas och renderas i jsdom.
vi.mock("next/navigation", () => ({
  redirect: (url: string) => redirect(url),
  useRouter: () => ({ push: vi.fn(), back: vi.fn() }),
}));

async function renderModal() {
  const element = await InterceptedCvImportModal();
  return render(element);
}

describe("@modal/(.)cv/importera intercepting route", () => {
  beforeEach(() => {
    redirect.mockReset();
    getServerSession.mockReset();
  });

  it("redirectar till /logga-in när användaren saknar session", async () => {
    getServerSession.mockResolvedValue(null);
    // redirect() kastar inte i mocken → render fortsätter, men vi verifierar
    // att auth-grinden anropade redirect korrekt.
    await renderModal();
    expect(redirect).toHaveBeenCalledWith("/logga-in");
  });

  it("renderar RouteModalShell med titel 'Importera CV' + CvUploadForm för inloggad", async () => {
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
    await renderModal();

    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");

    const labelledby = dialog.getAttribute("aria-labelledby");
    expect(document.getElementById(labelledby!)).toHaveTextContent(
      "Importera CV"
    );

    // CvUploadForm-närvaro: dess upload-knapp (ärlig copy).
    expect(
      screen.getByRole("button", { name: "Ladda upp och granska CV" })
    ).toBeInTheDocument();
  });
});
