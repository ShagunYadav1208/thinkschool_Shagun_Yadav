using System.Diagnostics;
using System.Net.Sockets;

namespace ParkFlow.E2ETests;

/// <summary>
/// Every other test project in this solution talks to the app through
/// <c>WebApplicationFactory</c>'s in-memory TestServer — no OS process, no socket, no bytes
/// actually on a wire. That's correct for unit/integration tests, but it can't be the *whole*
/// story: this fixture launches the real, compiled <c>ParkFlow.Api.dll</c> as an actual child
/// process bound to a real loopback port, so the one E2E test in this pyramid
/// (day-31/piece1/README.md) exercises the app exactly as a real client would — Kestrel, real
/// TCP, real JSON over the wire, nothing short-circuited.
///
/// <c>ParkFlow.Api.dll</c> is located next to this test assembly's own output: the
/// ProjectReference to ParkFlow.Api.csproj in ParkFlow.E2ETests.csproj makes MSBuild copy it there
/// as part of the normal build, so this needs no fragile relative-path traversal into ../../src/...
/// Config is passed via environment variables rather than relying on appsettings.Development.json
/// having been copied alongside it — keeps this fixture correct regardless of what content-copying
/// a given MSBuild/SDK version does for a test project's project references.
/// </summary>
public sealed class RealApiProcessFixture : IAsyncLifetime
{
    public const string ApiKey = "e2e-test-key-not-a-real-secret";
    public const string JwtSigningKey = "e2e-test-jwt-signing-key-not-a-real-secret-32-bytes-min";

    public HttpClient Client { get; private set; } = null!;

    private Process? _process;

    public async Task InitializeAsync()
    {
        var port = GetFreeLoopbackPort();
        var apiDllPath = Path.Combine(AppContext.BaseDirectory, "ParkFlow.Api.dll");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { apiDllPath },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.Environment["Security__ApiKey"] = ApiKey;
        startInfo.Environment["Security__Jwt__SigningKey"] = JwtSigningKey;

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {apiDllPath} as a child process.");

        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        await WaitUntilHealthyAsync();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();

        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
        }

        _process?.Dispose();
        return Task.CompletedTask;
    }

    private async Task WaitUntilHealthyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                var stderr = await _process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"ParkFlow.Api exited early (code {_process.ExitCode}) while starting for the E2E test:\n{stderr}");
            }

            try
            {
                var response = await Client.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException)
            {
                lastError = ex;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("ParkFlow.Api did not become healthy within 30s.", lastError);
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
