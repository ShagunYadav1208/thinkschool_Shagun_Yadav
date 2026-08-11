# AI reflection

Claude’s useful contribution was identifying validation as the part of the old order service that had the most reasons to change. Splitting request shape, quantity, and shipping checks into `IOrderValidationRule` strategies made the service’s responsibility obvious: run the configured rules. Adding a new rule now means registering another strategy instead of editing a long method with unrelated pricing and persistence logic. I rejected a larger proposal that introduced a mediator, a rule factory, and a separate result hierarchy; those abstractions would have hidden a simple loop and made the failure path harder to explain.

The bug I specifically looked for was rule ordering. `ItemQuantityRule` assumes `Items` is non-null, so it must run after `RequestShapeRule`; otherwise malformed requests could fail with `NullReferenceException` instead of the domain validation error. I also checked the boundary values 0, 1, 100, and 101 rather than trusting a broad “positive quantity” assertion.

Copilot-style suggestions saved typing for the three nearly identical quantity tests and reminded me to include the valid control case. One subtly wrong suggestion used `quantity >= 0`, which would have accepted zero even though zero cannot produce a meaningful order. I changed it to `quantity > 0` and kept the explicit zero test.

At 2 AM I would reach for production logs and a failing test first. Between the two assistants, I would use Claude for understanding a cross-file design problem, then Copilot for small, local test cases. Neither gets authority to decide the invariant.
