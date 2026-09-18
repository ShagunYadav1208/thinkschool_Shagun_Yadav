namespace ParkFlow.Api.Security;

/// <summary>
/// Fixed, well-known demo user ids for the <c>Development</c>-only token endpoint (see
/// <c>Program.cs</c> and ADR-001 / BUILD-PLAN.md, day-28/piece1). Real identity comes from a real
/// IdP eventually (Build Day 32); this is a stand-in, never usable outside <c>Development</c>.
/// </summary>
public static class DemoUsers
{
    public static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly IReadOnlyDictionary<string, Guid> ByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
    {
        ["userA"] = UserA,
        ["userB"] = UserB,
    };
}
