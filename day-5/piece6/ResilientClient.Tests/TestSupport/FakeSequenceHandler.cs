namespace ResilientClient.Tests.TestSupport;

/// <summary>
/// Stands in for the real downstream service at the innermost (primary) HttpMessageHandler
/// position, so the resilience pipeline registered in Program.cs runs unmodified around it — no
/// real socket, no real network, fully deterministic call counting.
/// </summary>
public sealed class FakeSequenceHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _callCount;

    public int CallCount => _callCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var callNumber = Interlocked.Increment(ref _callCount);
        var response = respond(callNumber);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
