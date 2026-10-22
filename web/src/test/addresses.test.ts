import { describe, expect, it } from "vitest";
import { parseAddresses } from "@/lib/addresses";

describe("parseAddresses", () => {
  it("splits on commas, semicolons, spaces and new lines", () => {
    expect(
      parseAddresses("a@example.com, b@example.com;c@example.com\nd@example.com e@example.com"),
    ).toEqual([
      "a@example.com",
      "b@example.com",
      "c@example.com",
      "d@example.com",
      "e@example.com",
    ]);
  });

  it("takes the address out of an Outlook header and drops the names", () => {
    expect(parseAddresses('Jane Doe <jane@example.com>; "Smith, John" <john@example.com>')).toEqual(
      ["jane@example.com", "john@example.com"],
    );
  });

  it("drops repeats regardless of case", () => {
    expect(parseAddresses("Jane@Example.com jane@example.com")).toEqual(["Jane@Example.com"]);
  });

  it("passes a malformed address through for the API to judge", () => {
    expect(parseAddresses("jane@ ok@example.com")).toEqual(["jane@", "ok@example.com"]);
  });

  it("returns nothing for blank text", () => {
    expect(parseAddresses("  \n ; , ")).toEqual([]);
  });
});
