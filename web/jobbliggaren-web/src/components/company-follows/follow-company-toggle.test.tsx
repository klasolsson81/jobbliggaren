import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FollowCompanyToggle } from "./follow-company-toggle";

const followActionMock = vi.fn();
const unfollowActionMock = vi.fn();

vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyFromJobAdAction: (...args: unknown[]) => followActionMock(...args),
  unfollowCompanyAction: (...args: unknown[]) => unfollowActionMock(...args),
}));

beforeEach(() => {
  followActionMock.mockReset();
  unfollowActionMock.mockReset();
});

describe("FollowCompanyToggle", () => {
  it("renders 'Bevaka företaget' when not following", () => {
    render(<FollowCompanyToggle jobAdId="j1" initialCompanyWatchId={null} />);
    expect(
      screen.getByRole("button", { name: "Bevaka företaget" })
    ).toBeInTheDocument();
    expect(screen.getByText("Bevaka företaget")).toBeInTheDocument();
  });

  it("renders 'Bevakar företaget' when already following", () => {
    render(<FollowCompanyToggle jobAdId="j1" initialCompanyWatchId="cw1" />);
    expect(
      screen.getByRole("button", { name: "Bevakar företaget" })
    ).toBeInTheDocument();
    expect(screen.getByText("Bevakar företaget")).toBeInTheDocument();
    // #1000 (V1) — teal state-tint when following (matches the BEVAKAR tag).
    expect(
      screen.getByRole("button", { name: "Bevakar företaget" })
    ).toHaveClass("jp-btn--on-follow");
  });

  it("uses the visible label as the accessible name in both states (WCAG 2.5.3, no divergent aria verb)", () => {
    // Fresh mounts, not rerender: `following` seeds from the prop via useState (mount-only).
    const { unmount } = render(
      <FollowCompanyToggle jobAdId="j1" initialCompanyWatchId={null} />
    );
    expect(screen.getByRole("button")).toHaveAccessibleName("Bevaka företaget");
    unmount();
    render(<FollowCompanyToggle jobAdId="j1" initialCompanyWatchId="cw1" />);
    expect(screen.getByRole("button")).toHaveAccessibleName("Bevakar företaget");
  });

  it("calls followCompanyFromJobAdAction with the jobAdId and flips to following", async () => {
    followActionMock.mockResolvedValue({ success: true, companyWatchId: "cw-new" });
    render(<FollowCompanyToggle jobAdId="j1" initialCompanyWatchId={null} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Bevaka företaget" })
    );

    expect(followActionMock).toHaveBeenCalledWith("j1");
    expect(await screen.findByText("Bevakar företaget")).toBeInTheDocument();
  });

  it("calls unfollowCompanyAction with the CompanyWatchId when following", async () => {
    unfollowActionMock.mockResolvedValue({ success: true });
    render(<FollowCompanyToggle jobAdId="j1" initialCompanyWatchId="cw1" />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Bevakar företaget" })
    );

    expect(unfollowActionMock).toHaveBeenCalledWith("cw1");
    expect(await screen.findByText("Bevaka företaget")).toBeInTheDocument();
  });

  it("rolls back to 'Bevaka företaget' when follow fails", async () => {
    followActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte bevaka företaget. Försök igen.",
    });
    render(<FollowCompanyToggle jobAdId="j1" initialCompanyWatchId={null} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Bevaka företaget" })
    );

    expect(
      await screen.findByText(/Kunde inte bevaka företaget/i)
    ).toBeInTheDocument();
    expect(screen.getByText("Bevaka företaget")).toBeInTheDocument();
  });

  it("rolls back to 'Bevakar företaget' when unfollow fails", async () => {
    unfollowActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte sluta bevaka företaget. Försök igen.",
    });
    render(<FollowCompanyToggle jobAdId="j1" initialCompanyWatchId="cw1" />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Bevakar företaget" })
    );

    expect(
      await screen.findByText(/Kunde inte sluta bevaka företaget/i)
    ).toBeInTheDocument();
    expect(screen.getByText("Bevakar företaget")).toBeInTheDocument();
  });

  it("uses the id from a successful follow when unfollowing next", async () => {
    followActionMock.mockResolvedValue({ success: true, companyWatchId: "cw-resolved" });
    unfollowActionMock.mockResolvedValue({ success: true });
    render(<FollowCompanyToggle jobAdId="j1" initialCompanyWatchId={null} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Bevaka företaget" })
    );
    await screen.findByText("Bevakar företaget");

    await user.click(
      screen.getByRole("button", { name: "Bevakar företaget" })
    );

    expect(unfollowActionMock).toHaveBeenCalledWith("cw-resolved");
    expect(await screen.findByText("Bevaka företaget")).toBeInTheDocument();
  });
});
