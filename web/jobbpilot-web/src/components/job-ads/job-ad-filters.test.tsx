import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobAdFilters } from "./job-ad-filters";
import type { JobAdFiltersValues } from "@/lib/dto/job-ads";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

const initial: JobAdFiltersValues = {
  ssyk: "",
  region: "",
  q: "",
  sortBy: "PublishedAtDesc",
};

describe("JobAdFilters", () => {
  beforeEach(() => {
    pushMock.mockReset();
  });

  it("renders all filter fields with labels", () => {
    render(<JobAdFilters initial={initial} />);
    expect(screen.getByLabelText("Sökord")).toBeInTheDocument();
    expect(screen.getByLabelText("SSYK-kod")).toBeInTheDocument();
    expect(screen.getByLabelText("Region")).toBeInTheDocument();
    expect(screen.getByLabelText("Sortering")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Filtrera" })).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Återställ" })
    ).toBeInTheDocument();
  });

  it("submits filter and pushes URL with non-empty params", async () => {
    const user = userEvent.setup();
    render(<JobAdFilters initial={initial} />);

    await user.type(screen.getByLabelText("Sökord"), "backend");
    await user.click(screen.getByRole("button", { name: "Filtrera" }));

    await waitFor(() => expect(pushMock).toHaveBeenCalled());
    expect(pushMock).toHaveBeenCalledWith("/jobb?q=backend");
  });

  it("includes ssyk + region in URL when filled", async () => {
    const user = userEvent.setup();
    render(<JobAdFilters initial={initial} />);

    await user.type(screen.getByLabelText("SSYK-kod"), "MVqp_eS8_kDZ");
    await user.type(screen.getByLabelText("Region"), "CifL_Rzy_Mku");
    await user.click(screen.getByRole("button", { name: "Filtrera" }));

    await waitFor(() => expect(pushMock).toHaveBeenCalled());
    expect(pushMock).toHaveBeenCalledWith(
      "/jobb?ssyk=MVqp_eS8_kDZ&region=CifL_Rzy_Mku"
    );
  });

  it("pushes /jobb (no query) when all fields empty", async () => {
    const user = userEvent.setup();
    render(<JobAdFilters initial={initial} />);
    await user.click(screen.getByRole("button", { name: "Filtrera" }));
    await waitFor(() => expect(pushMock).toHaveBeenCalled());
    expect(pushMock).toHaveBeenCalledWith("/jobb");
  });

  it("rejects q with 1 char and shows error (matches backend validator)", async () => {
    const user = userEvent.setup();
    render(<JobAdFilters initial={initial} />);

    await user.type(screen.getByLabelText("Sökord"), "a");
    await user.click(screen.getByRole("button", { name: "Filtrera" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /Söktexten måste vara 2–100 tecken/
    );
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("rejects ssyk with invalid characters (defense-in-depth vs backend)", async () => {
    const user = userEvent.setup();
    render(<JobAdFilters initial={initial} />);

    await user.type(screen.getByLabelText("SSYK-kod"), "ssyk!hack");
    await user.click(screen.getByRole("button", { name: "Filtrera" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /SSYK-koden måste vara 1–32 tecken/
    );
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("Återställ pushes plain /jobb", async () => {
    const user = userEvent.setup();
    render(
      <JobAdFilters
        initial={{ ...initial, q: "backend", ssyk: "MVqp_eS8_kDZ" }}
      />
    );

    await user.click(screen.getByRole("button", { name: "Återställ" }));
    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/jobb"));
  });
});
