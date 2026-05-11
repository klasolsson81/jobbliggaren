// Re-export från lib/dto/admin.ts. Zod-schemat är single source of truth
// per ADR 0020. Nya konsumenter bör importera från `@/lib/dto/admin`.
export type {
  AuditLogEntryDto,
  AuditLogPagedResult,
  AuditLogFilter,
} from "@/lib/dto/admin";
