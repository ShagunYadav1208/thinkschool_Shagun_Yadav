
# WHY.md

The anemic `Quote` was just a property bag; every layer that touched it had to reimplement "is this valid" from scratch. That's exactly how the null-author bug (found in day-1/piece3 and day-2/piece1) happened: the endpoint checked `IsNullOrWhiteSpace(request.Author)` then fell through to an unconditional `request.Author.Length > 100` check instead of `else if`. Send `{"author": null, "text": "hi"}` and it 500s with a `NullReferenceException` instead of a clean 400 — because the null-guard lived in the endpoint, copy-pasted, and one copy dropped the `else`. The bug was never in the business rule; it was in duplicating the rule.

`Quote.Create()` moves that rule into one place the entity itself owns. Every caller — this endpoint, a future gRPC handler, a background import job — gets the same guarantee for free: `author?.Trim() ?? string.Empty` before any length check, so null can never reach `.Length`. There's no second copy to get wrong.

Immutability matters differently: `Text` has no setter after construction, so "can a quote's text silently change" isn't a runtime question anymore — the compiler answers it. Soft-delete as an explicit `Delete()` method, not a hard `DbSet.Remove`, makes deletion a domain event with its own invariant (can't delete twice), not a SQL side effect anyone could trigger differently.
