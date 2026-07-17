import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ResumeContentForm } from "./resume-content-form";
import { emptyContent } from "@/lib/resumes/content-utils";
import type { ResumeContentDto } from "@/lib/types/resumes";
import type { ActionResult } from "@/lib/actions/resumes";

const updateMasterContentActionMock = vi.fn<
  (resumeId: string, content: ResumeContentDto) => Promise<ActionResult>
>();

vi.mock("@/lib/actions/resumes", () => ({
  updateMasterContentAction: (resumeId: string, content: ResumeContentDto) =>
    updateMasterContentActionMock(resumeId, content),
}));

const RESUME_ID = "550e8400-e29b-41d4-a716-446655440000";

function withFullName(name = "Anna Andersson"): ResumeContentDto {
  return emptyContent(name);
}

describe("ResumeContentForm", () => {
  beforeEach(() => {
    updateMasterContentActionMock.mockReset();
    updateMasterContentActionMock.mockResolvedValue({ success: true });
  });

  it("renders minimal content with personal info pre-populated", () => {
    render(
      <ResumeContentForm
        resumeId={RESUME_ID}
        initialContent={withFullName()}
      />
    );

    expect(screen.getByLabelText("Fullständigt namn")).toHaveValue(
      "Anna Andersson"
    );
    expect(screen.getByRole("heading", { name: "Erfarenhet" })).toBeInTheDocument();
    // Lock all three empty-state copies (per CLAUDE.md §10.3 / DESIGN.md §8.4).
    expect(screen.getByText("Ingen erfarenhet tillagd.")).toBeInTheDocument();
    expect(screen.getByText("Ingen utbildning tillagd.")).toBeInTheDocument();
    expect(screen.getByText("Inga färdigheter tillagda.")).toBeInTheDocument();
  });

  it("appends an experience fieldset on 'Lägg till erfarenhet'", async () => {
    const user = userEvent.setup();
    render(
      <ResumeContentForm
        resumeId={RESUME_ID}
        initialContent={withFullName()}
      />
    );

    await user.click(
      screen.getByRole("button", { name: "Lägg till erfarenhet" })
    );

    expect(screen.getByLabelText("Företag")).toBeInTheDocument();
    expect(screen.getByLabelText("Roll")).toBeInTheDocument();
    expect(screen.queryByText("Ingen erfarenhet tillagd.")).toBeNull();
  });

  it("submits master content and shows 'Sparat' on success", async () => {
    const user = userEvent.setup();
    render(
      <ResumeContentForm
        resumeId={RESUME_ID}
        initialContent={withFullName()}
      />
    );

    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    await waitFor(() => {
      expect(updateMasterContentActionMock).toHaveBeenCalledTimes(1);
    });
    const call = updateMasterContentActionMock.mock.calls[0];
    if (!call) throw new Error("updateMasterContentAction was not invoked");
    expect(call[0]).toBe(RESUME_ID);
    expect(call[1].personalInfo.fullName).toBe("Anna Andersson");

    // Lock the 24h locale-aware time: "Sparat HH:MM." via the shared
    // formatTime util (#214; locale-aware, hour12 pinned false — CLAUDE.md §10).
    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent(/Sparat \d{2}:\d{2}/);
  });

  it("shows server error when action returns { success:false, error }", async () => {
    updateMasterContentActionMock.mockResolvedValueOnce({
      success: false,
      error: "Kunde inte spara CV.",
    });

    const user = userEvent.setup();
    render(
      <ResumeContentForm
        resumeId={RESUME_ID}
        initialContent={withFullName()}
      />
    );

    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Kunde inte spara CV.");
  });

  it("TD-15 path-routing: schema-fail on experience.company focuses the array field", async () => {
    // Exercises pathToElementId on an array path: "experiences.0.company"
    // → "exp-0-company". Without this, only the personalInfo.* branch is
    // tested, and the regex-based array-path mapping is silently regression-prone.
    const user = userEvent.setup();
    render(
      <ResumeContentForm
        resumeId={RESUME_ID}
        initialContent={withFullName()}
      />
    );

    await user.click(
      screen.getByRole("button", { name: "Lägg till erfarenhet" })
    );

    const company = screen.getByLabelText("Företag");
    const role = screen.getByLabelText("Roll");
    const startDate = screen.getByLabelText("Startdatum (valfritt)");
    // Strip required on all submit-blocking fields — schema's .min(1) is what
    // we want to trigger. company is first in experienceSchema declaration
    // order so issues[0] owns "experiences.0.company".
    company.removeAttribute("required");
    role.removeAttribute("required");
    startDate.removeAttribute("required");
    await user.type(role, "Utvecklare");
    await user.type(startDate, "2024-01-01");
    // Leave company empty — triggers "Företag krävs." with path
    // ["experiences", 0, "company"] → pathToElementId → "exp-0-company".

    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    await waitFor(() => {
      expect(company).toHaveAttribute("aria-invalid", "true");
    });
    expect(company).toHaveAttribute("id", "exp-0-company");
    expect(company).toHaveFocus();
    expect(updateMasterContentActionMock).not.toHaveBeenCalled();
  });

  it("TD-15: schema-invalid fullName sets aria-invalid + focuses the field", async () => {
    const user = userEvent.setup();
    render(
      <ResumeContentForm
        resumeId={RESUME_ID}
        initialContent={withFullName()}
      />
    );

    const fullNameField = screen.getByLabelText("Fullständigt namn");
    // Remove HTML5 'required' so the form actually submits — we want to verify
    // schema-level rejection (.trim().min(1)) not the browser's required gate.
    fullNameField.removeAttribute("required");
    await user.clear(fullNameField);
    await user.type(fullNameField, "   ");

    await user.click(screen.getByRole("button", { name: "Spara CV" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveAttribute("id", "content-form-error");

    await waitFor(() => {
      expect(fullNameField).toHaveAttribute("aria-invalid", "true");
    });
    expect(fullNameField).toHaveAttribute("aria-describedby", "content-form-error");
    expect(fullNameField).toHaveFocus();
    expect(updateMasterContentActionMock).not.toHaveBeenCalled();
  });
});
