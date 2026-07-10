import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusDot } from "@/components/StatusDot";

describe("StatusDot", () => {
  it("labels each status for screen readers", () => {
    render(<StatusDot status="operational" />);
    expect(screen.getByLabelText("Operational")).toBeInTheDocument();
  });

  it("renders the paused icon when paused, regardless of status", () => {
    render(<StatusDot status="down" paused />);
    expect(screen.getByLabelText("Paused")).toBeInTheDocument();
    expect(screen.queryByLabelText("Down")).not.toBeInTheDocument();
  });

  it("colors the dot by status", () => {
    render(<StatusDot status="degraded" />);
    expect(screen.getByLabelText("Degraded")).toHaveStyle({
      backgroundColor: "var(--color-warning)",
    });
  });
});
