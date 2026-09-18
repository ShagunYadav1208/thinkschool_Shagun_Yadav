namespace ParkFlow.Api.Security;

/// <summary>Body for the <c>Development</c>-only <c>POST /api/v1/dev/token</c> endpoint.</summary>
/// <param name="User">One of the names in <see cref="DemoUsers.ByName"/> (e.g. "userA").</param>
public sealed record DevTokenRequest(string User);
