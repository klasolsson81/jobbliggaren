export interface AuditLogEntryDto {
  id: string;
  occurredAt: string;
  correlationId: string;
  userId: string | null;
  impersonatedBy: string | null;
  eventType: string;
  aggregateType: string;
  aggregateId: string;
  ipAddress: string | null;
  userAgent: string | null;
}

export interface AuditLogPagedResult {
  items: AuditLogEntryDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface AuditLogFilter {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  userId?: string;
  eventType?: string;
  aggregateType?: string;
}
