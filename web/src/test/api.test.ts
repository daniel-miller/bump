import { describe, expect, it } from "vitest";
import { ApiError, problemMessage } from "@/lib/api";

describe("problemMessage", () => {
  it("prefers the problem detail over the status text", () => {
    const err = new ApiError(409, "Conflict", {
      title: "Handle already exists",
      detail: "Another service already has the handle 'vesper-api'.",
    });
    expect(problemMessage(err)).toBe("Another service already has the handle 'vesper-api'.");
  });

  it("falls back to the problem title", () => {
    expect(problemMessage(new ApiError(409, "Conflict", { title: "Handle already exists" }))).toBe(
      "Handle already exists",
    );
  });

  it("falls back to the status text when there is no problem body", () => {
    expect(problemMessage(new ApiError(502, "Bad Gateway"))).toBe("Bad Gateway");
  });

  it("reads a plain error's message", () => {
    expect(problemMessage(new Error("Network down"))).toBe("Network down");
  });
});
