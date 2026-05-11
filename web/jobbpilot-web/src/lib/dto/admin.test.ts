import { describe, it, expect } from "vitest";
import {
  auditLogEntryDtoSchema,
  auditLogPagedResultSchema,
} from "./admin";

const validEntry = {
  id: "11111111-1111-1111-1111-111111111111",
  occurredAt: "2026-05-11T10:00:00Z",
  correlationId: "corr-1",
  userId: "22222222-2222-2222-2222-222222222222",
  impersonatedBy: null,
  eventType: "ApplicationCreated",
  aggregateType: "Application",
  aggregateId: "33333333-3333-3333-3333-333333333333",
  ipAddress: "127.0.0.1",
  userAgent: "Mozilla/5.0",
};

describe("auditLogEntryDtoSchema", () => {
  it("accepts valid entry", () => {
    expect(auditLogEntryDtoSchema.safeParse(validEntry).success).toBe(true);
  });

  it("accepts userId/ipAddress/userAgent as null", () => {
    const entry = {
      ...validEntry,
      userId: null,
      ipAddress: null,
      userAgent: null,
    };
    expect(auditLogEntryDtoSchema.safeParse(entry).success).toBe(true);
  });

  it("rejects when occurredAt missing", () => {
    const withoutOccurredAt: Partial<typeof validEntry> = { ...validEntry };
    delete withoutOccurredAt.occurredAt;
    expect(auditLogEntryDtoSchema.safeParse(withoutOccurredAt).success).toBe(
      false
    );
  });
});

describe("auditLogPagedResultSchema", () => {
  it("accepts valid paged result", () => {
    const result = {
      items: [validEntry],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    expect(auditLogPagedResultSchema.safeParse(result).success).toBe(true);
  });

  it("rejects when totalPages missing", () => {
    const result = {
      items: [validEntry],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
    expect(auditLogPagedResultSchema.safeParse(result).success).toBe(false);
  });

  it("rejects when item shape invalid", () => {
    const result = {
      items: [{ ...validEntry, eventType: 123 }],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    expect(auditLogPagedResultSchema.safeParse(result).success).toBe(false);
  });
});
