import { describe, it, expect } from "vitest";
import {
  classifyWord,
  excludedTermOf,
  positiveQ,
  Q_MAX_EXCLUDED_TERMS,
} from "./search-text-grammar";

// #2143 — the same table as the backend's SearchQueryParserTests, so the field and the
// server agree on which words exclude.
describe("classifyWord", () => {
  it.each([
    ["-deltid", "deltid"],
    ["--deltid", "deltid"],
    ["+-deltid", "deltid"],
    ['-"deltid"', "deltid"],
    ['-"-deltid"', "deltid"],
    ['-"+-deltid"', "deltid"],
    ['--"deltid"', "deltid"],
    ["-e-handel", "e-handel"],
    ["–deltid", "deltid"], // U+2013 en dash
    ["—deltid", "deltid"], // U+2014 em dash
    ["‐deltid", "deltid"], // U+2010 hyphen
    ["−deltid", "deltid"], // U+2212 minus sign
    ["－deltid", "deltid"], // U+FF0D fullwidth hyphen-minus
    ["-(-deltid", "deltid"],
    ["-!-deltid", "deltid"],
    ["-&-deltid", "deltid"],
    ["-|-deltid", "deltid"],
    ["-<-deltid", "deltid"],
    ["-)-deltid", "deltid"],
    ['-"(-deltid"', "deltid"],
  ])("%s excludes %s", (token, term) => {
    expect(classifyWord(token)).toEqual({ kind: "excluded", term });
  });

  it.each([
    ["e-handel", "e-handel"],
    ["Absolut-fjolljobb", "Absolut-fjolljobb"],
    ["+göteborg", "göteborg"],
    ["(-deltid", "(-deltid"],
  ])("%s is the positive word %s", (token, word) => {
    expect(classifyWord(token)).toEqual({ kind: "positive", word });
  });

  it("ignores control and format characters, as the server strips them before it splits", () => {
    expect(classifyWord("\u200B-deltid")).toEqual({ kind: "excluded", term: "deltid" });
    expect(classifyWord("lä\u200Brare")).toEqual({ kind: "positive", word: "lärare" });
  });

  it.each(["-", "--", "+", "+-", '-""', "-!!!", "-&*"])("%s is a bare operator", (token) => {
    expect(classifyWord(token)).toEqual({ kind: "operator" });
  });
});

describe("excludedTermOf", () => {
  it("returns the bare term of an exclusion and null otherwise", () => {
    expect(excludedTermOf("-deltid")).toBe("deltid");
    expect(excludedTermOf("deltid")).toBeNull();
  });
});

describe("positiveQ", () => {
  it("keeps only the positive words", () => {
    expect(positiveQ("lärare -deltid volvo -stockholm")).toBe("lärare volvo");
    expect(positiveQ("-deltid")).toBe("");
  });

  it("splits on commas as the server's parser does once a minus-word is present", () => {
    expect(positiveQ("systemutvecklare,-deltid")).toBe("systemutvecklare");
  });
});

describe("Q_MAX_EXCLUDED_TERMS", () => {
  it("mirrors ParsedSearchQuery.MaxExcludedTerms", () => {
    expect(Q_MAX_EXCLUDED_TERMS).toBe(5);
  });
});
