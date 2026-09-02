# Brief to the agent (Claude Code)

**Exercise (Day 20 - The outbox pattern):** a DB write and a queue publish must not diverge.
Implement the transactional outbox: write the domain change + an outbox row in one EF transaction,
then a relay publishes and marks sent. Prove no message is lost if the publish step crashes.

**Where:** `day-20/piece1`. Backend is [day-19/piece1](../../day-19/piece1)'s `QuotesApi`, copied
unmodified into [QuotesApi/](QuotesApi/) so the outbox table + relay could be added without
touching the read-only original. Frontend is day-19/piece1's Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) with one new tab added: **Outbox**.

**What changed from day-19's publish path:** day-19's `POST /api/quotes/` called the Service Bus
publisher inline, best-effort - a real gap, since the DB write and "the event was sent" could
silently diverge if the publish failed or the process died right after the commit. Day 20 closes
that gap: the domain write and an outbox row commit as one EF transaction; a separate relay
(`OutboxRelayService`) is now the only thing that ever calls the publisher, polling for unprocessed
rows on its own schedule, independent of any one HTTP request.

**Do not modify** `day-19/piece1` (or anything upstream of it) - read-only reference / copy source,
same rule every prior day has used. Anything needed from it was copied into `day-20/piece1` first,
never edited in place.

## Test isolation note

`day-19/piece1` was deployed live onto a shared App Service (`syquotes17-api`) that keeps its own
`AuditLogProcessorService`/`NotificationProcessorService` running continuously against the real
`audit-log`/`notifications` Service Bus subscriptions. Local testing of this day's crash scenarios
needed deterministic control over which process consumed each message (two independent competing
consumers could otherwise each grab one delivery and neither would see a duplicate - a false
negative on the exact thing being proven). With the user's explicit approval, the production
App Service was stopped for the duration of local crash-scenario testing and restarted immediately
after - see verification-log.md section 1.

## The exercise's own gates

- Paste the outbox table + relay - see README.md sections 1-2.
- Describe the crash scenario tested and why no message is lost or duplicated (at-least-once +
  idempotent consumer) - see README.md section 3, backed by a live, deterministic run in
  verification-log.md and the screenshots in [screenshots/](screenshots/).
