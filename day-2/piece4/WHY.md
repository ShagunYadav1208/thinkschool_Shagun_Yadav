# Why a rich Quote matters

The old Quote was an anemic data bag. Any caller could set Author or Text freely. Validation lived in the HTTP endpoint, so a background job, import script, or future API endpoint could create an invalid quote by bypassing that endpoint. The database could contain invalid values or altered published text. Every new caller had to remember the same rules.

The rich Quote keeps rules beside the state they protect. Quote.Create is the public creation path and returns either a valid quote or a domain error. It trims values and enforces the author and text limits. Text has a private setter and no update method, so code cannot silently edit a published quote. Deletion is expressed as SoftDelete, preserving the record while preventing it from appearing in active lookups. The endpoint translates the aggregate result into HTTP validation feedback.

For example, an admin bulk-import feature might previously instantiate a Quote with empty text and save it without using POST. The rich model makes that impossible through its public API: creation returns Text is required instead of an invalid entity. Invalid states are harder to represent, tests are faster, and future entry points stay safer.
