// Matches thinkschool_Shagun_Yadav/day-19/piece1/QuotesApi/ServiceBusMessaging/*.cs exactly -
// verified live against the running API. ASP.NET Core's minimal-API JSON serialization uses
// camelCase by default, confirmed by curl against the real endpoints.

export interface AuditLogEntry {
  messageId: string;
  quoteId: number;
  author: string;
  handledBy: string;
  wasDuplicate: boolean;
  handledAt: string;
}

export interface NotificationEntry {
  messageId: string;
  quoteId: number;
  message: string;
  wasDuplicate: boolean;
  handledAt: string;
}

export interface DeadLetterMessage {
  messageId: string;
  body: string;
  deliveryCount: number;
  deadLetterReason: string | null;
  deadLetterErrorDescription: string | null;
  enqueuedTime: string;
}

export interface QuoteCreatedEventDto {
  eventId: string;
  quoteId: number;
  author: string;
  text: string;
  createdAt: string;
}
