# Brief to the agent (Claude Code)

**Exercise (Day 19 - Azure Service Bus topics + DLQ):** publish to a Service Bus topic with two
subscriptions, consume with a competing-consumer worker, make handlers idempotent (dedupe on a
message id), and demonstrate the dead-letter queue catching a poison message.

**Where:** `day-19/piece1`. Backend is [day-18/piece1](../../day-18/piece1)'s `QuotesApi`, copied
unmodified into [QuotesApi/](QuotesApi/) so Service Bus publishing/consuming could be added without
touching the read-only original. Frontend is day-18/piece1's Angular app, copied unmodified into
[quotes-list-detail/](quotes-list-detail/) with one new tab added: **Service Bus**.

**Real Azure Service Bus, not a simulation** - explicitly chosen over the two alternatives offered
(a local Docker emulator, or an honestly-labeled in-process simulation) because Basic-tier Service
Bus doesn't support topics/subscriptions at all, and the exercise specifically asks for topics with
two subscriptions plus a real DLQ. Provisioned real, Standard-tier resources under the
`Azure for Students` subscription - see README.md section 1 for exactly what and why, and the note
at the top about ongoing cost.

**Do not modify** `day-18/piece1` (or anything upstream of it) - read-only reference / copy source,
same rule every prior day has used. Anything needed from it was copied into `day-19/piece1` first,
never edited in place.

## The exercise's own gates

- Paste the publisher, the consumer, the idempotency key handling, and proof a poison message
  landed in the DLQ - see README.md sections 2-4, all backed by a live run captured in
  [verification-log.md](verification-log.md) and the screenshots in
  [screenshots/](screenshots/).
