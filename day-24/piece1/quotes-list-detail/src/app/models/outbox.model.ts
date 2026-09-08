// Matches thinkschool_Shagun_Yadav/day-20/piece1/QuotesApi/Models/OutboxMessage.cs (the shape
// GET /api/outbox actually projects, not the full entity) - verified live against the running API.
export interface OutboxRow {
  id: string;
  quoteId: number;
  eventType: string;
  createdAt: string;
  processedAt: string | null;
  attempts: number;
}
