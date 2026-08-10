
Create a deliberately bad legacy-style OrderController.cs for an ASP.NET Core 10 application.

Requirements:

- Approximately 300 lines of code.
- Create one giant POST /api/orders action.
- Put business logic, validation, EF Core data access, calculations, and HTTP response shaping directly inside the controller action.
- Use synchronous EF Core calls such as ToList(), FirstOrDefault(), Find(), SaveChanges(), etc. inside an async action.
- Make the action return object instead of typed IActionResult/ActionResult<T></t>/typed HTTP results.
- Include four separate empty catch { } blocks that swallow exceptions.
- Include at least two subtle bugs:
  1. An off-by-one error.
  2. A possible null dereference.
- Do not use a service layer.
- Do not use a repository layer.
- Do not properly separate responsibilities.
- Include poor validation practices.
- Include direct database access from the controller.
- Include duplicated logic where reasonable.
- Include hard-coded values/magic numbers where reasonable.
- Use poor naming in a few places.
- Include zero tests.
- Make the code compile as a realistic ASP.NET Core 10 controller.
- Use realistic Order, OrderItem, Product, and database-related models or assume reasonable existing model types.
- Make the code intentionally difficult to maintain but still believable as code written in a real legacy application.

IMPORTANT:
Return ONLY the contents of OrderController.cs.
Do not explain the smells.
Do not refactor it.
Do not improve it.
