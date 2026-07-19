import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyFollowButton } from "./company-follow-button";

const followActionMock = vi.fn();
const unfollowActionMock = vi.fn();

vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: (...args: unknown[]) => followActionMock(...args),
  unfollowCompanyAction: (...args: unknown[]) => unfollowActionMock(...args),
}));

beforeEach(() => {
  followActionMock.mockReset();
  unfollowActionMock.mockReset();
});

const ORG_NR = "5592804784";
const COMPANY = "Acme Bygg AB";

describe("CompanyFollowButton", () => {
  it("renders 'Bevaka' with a company-specific accessible name when not following", () => {
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId={null}
      />
    );
    expect(
      screen.getByRole("button", { name: `Bevaka ${COMPANY}` })
    ).toBeInTheDocument();
    expect(screen.getByText("Bevaka")).toBeInTheDocument();
  });

  it("renders 'Bevakar' with a company-specific accessible name when already following", () => {
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId="cw1"
      />
    );
    expect(
      screen.getByRole("button", { name: `Sluta bevaka ${COMPANY}` })
    ).toBeInTheDocument();
    expect(screen.getByText("Bevakar")).toBeInTheDocument();
  });

  it("calls followCompanyAction with the org.nr and flips to following", async () => {
    followActionMock.mockResolvedValue({ success: true, companyWatchId: "cw-new" });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId={null}
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Bevaka ${COMPANY}` }));

    expect(followActionMock).toHaveBeenCalledWith(ORG_NR);
    expect(await screen.findByText("Bevakar")).toBeInTheDocument();
  });

  it("calls unfollowCompanyAction with the CompanyWatchId when following", async () => {
    unfollowActionMock.mockResolvedValue({ success: true });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId="cw1"
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Sluta bevaka ${COMPANY}` }));

    expect(unfollowActionMock).toHaveBeenCalledWith("cw1");
    expect(await screen.findByText("Bevaka")).toBeInTheDocument();
  });

  it("rolls back to 'Bevaka' when follow fails", async () => {
    followActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte bevaka företaget. Försök igen.",
    });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId={null}
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Bevaka ${COMPANY}` }));

    expect(
      await screen.findByText(/Kunde inte bevaka företaget/i)
    ).toBeInTheDocument();
    expect(screen.getByText("Bevaka")).toBeInTheDocument();
  });

  it("rolls back to 'Bevakar' when unfollow fails", async () => {
    unfollowActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte sluta bevaka företaget. Försök igen.",
    });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId="cw1"
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Sluta bevaka ${COMPANY}` }));

    expect(
      await screen.findByText(/Kunde inte sluta bevaka företaget/i)
    ).toBeInTheDocument();
    expect(screen.getByText("Bevakar")).toBeInTheDocument();
  });

  it("uses the id from a successful follow when unfollowing next", async () => {
    followActionMock.mockResolvedValue({ success: true, companyWatchId: "cw-resolved" });
    unfollowActionMock.mockResolvedValue({ success: true });
    render(
      <CompanyFollowButton
        orgNr={ORG_NR}
        companyName={COMPANY}
        initialCompanyWatchId={null}
      />
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: `Bevaka ${COMPANY}` }));
    await screen.findByText("Bevakar");

    await user.click(screen.getByRole("button", { name: `Sluta bevaka ${COMPANY}` }));

    expect(unfollowActionMock).toHaveBeenCalledWith("cw-resolved");
    expect(await screen.findByText("Bevaka")).toBeInTheDocument();
  });
});
