using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexEdgeWidget;

public static class ClientIntegrationTests
{
    private const string LogVariable = "CODEX_EDGE_TEST_FIXTURE_LOG";
    private const string ModeVariable = "CODEX_EDGE_TEST_FIXTURE_MODE";
    private const string MarkerVariable = "CODEX_EDGE_TEST_FIXTURE_MARKER";
    private sealed record FixtureEvent(int Pid, long StartedTicks, string Event, string? Method);

    public static void Run(Action<string, Action> check)
    {
        check("client sends quota-only protocol and reuses its owned connection", () => Scenario("normal", async fixture =>
        {
            await using (var client = new QuotaClient(FixtureExecutable()))
            {
                AssertQuota(await client.FetchAsync(CancellationToken.None));
                AssertQuota(await client.FetchAsync(CancellationToken.None));
            }
            var events = fixture.ReadEvents();
            Equal(1, events.Count(item => item.Event == "started"));
            var methods = events.Where(item => item.Event == "rpc").Select(item => item.Method).ToArray();
            Equal("initialize,initialized,account/rateLimits/read,account/rateLimits/read", string.Join(",", methods));
            await fixture.AssertAllChildrenStopped();
        }));

        check("concurrent refreshes share one app-server without racing startup", () => Scenario("normal", async fixture =>
        {
            await using (var client = new QuotaClient(FixtureExecutable()))
            {
                var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.FetchAsync(CancellationToken.None)));
                foreach (var result in results) AssertQuota(result);
            }
            var events = fixture.ReadEvents();
            Equal(1, events.Count(item => item.Event == "started"));
            Equal(1, events.Count(item => item.Method == "initialize"));
            Equal(3, events.Count(item => item.Method == "account/rateLimits/read"));
            await fixture.AssertAllChildrenStopped();
        }));

        check("caller cancellation during initialize reclaims the owned app-server", () => Scenario("hang-initialize", async fixture =>
        {
            await using (var client = new QuotaClient(FixtureExecutable()))
            using (var cancellation = new CancellationTokenSource())
            {
                var fetch = client.FetchAsync(cancellation.Token);
                await fixture.WaitForMethod("initialize");
                cancellation.Cancel();
                await ExpectCancelled(fetch.WaitAsync(TimeSpan.FromSeconds(3)));
            }
            Equal(0, fixture.ReadEvents().Count(item => item.Method == "account/rateLimits/read"));
            await fixture.AssertAllChildrenStopped();
        }));

        check("dispose cancels an in-flight quota request without deadlock or child leak", () => Scenario("hang-quota", async fixture =>
        {
            var client = new QuotaClient(FixtureExecutable());
            try
            {
                var fetch = client.FetchAsync(CancellationToken.None);
                await fixture.WaitForMethod("account/rateLimits/read");
                await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                await ExpectCancelled(fetch.WaitAsync(TimeSpan.FromSeconds(1)));
                await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            }
            finally { await client.DisposeAsync(); }
            await fixture.AssertAllChildrenStopped();
        }));

        check("caller cancellation during quota read leaves no owned child", () => Scenario("hang-quota", async fixture =>
        {
            await using (var client = new QuotaClient(FixtureExecutable()))
            using (var cancellation = new CancellationTokenSource())
            {
                var fetch = client.FetchAsync(cancellation.Token);
                await fixture.WaitForMethod("account/rateLimits/read");
                cancellation.Cancel();
                await ExpectCancelled(fetch.WaitAsync(TimeSpan.FromSeconds(3)));
            }
            await fixture.AssertAllChildrenStopped();
        }));

        check("failed app-server connection can reconnect on the next refresh", () => Scenario("fault-once", async fixture =>
        {
            await using (var client = new QuotaClient(FixtureExecutable()))
            {
                var failed = false;
                try { await client.FetchAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (IOException) { failed = true; }
                if (!failed) throw new Exception("Fixture's first connection should fail.");
                AssertQuota(await client.FetchAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)));
            }
            var events = fixture.ReadEvents();
            Equal(2, events.Count(item => item.Event == "started"));
            Equal(2, events.Count(item => item.Method == "initialize"));
            await fixture.AssertAllChildrenStopped();
        }));
    }

    public static async Task<int> RunFixtureAsync(string[] args)
    {
        var log = Environment.GetEnvironmentVariable(LogVariable);
        var mode = Environment.GetEnvironmentVariable(ModeVariable) ?? "normal";
        var marker = Environment.GetEnvironmentVariable(MarkerVariable);
        if (string.IsNullOrEmpty(log) || !args.Contains("--stdio")) return 31;
        var pid = Environment.ProcessId;
        var started = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        void Log(string name, string? method = null)
            => File.AppendAllText(log, JsonSerializer.Serialize(new FixtureEvent(pid, started, name, method)) + Environment.NewLine);

        Log("started");
        try
        {
            while (await Console.In.ReadLineAsync() is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var request = document.RootElement;
                var method = request.GetProperty("method").GetString() ?? "";
                Log("rpc", method);
                if (method == "initialized") continue;
                var id = request.GetProperty("id").GetInt64();
                if (method == "initialize")
                {
                    if (mode == "hang-initialize") await Task.Delay(Timeout.InfiniteTimeSpan);
                    await Reply(new { id, result = new { } });
                }
                else if (method == "account/rateLimits/read")
                {
                    if (mode == "hang-quota") await Task.Delay(Timeout.InfiniteTimeSpan);
                    if (mode == "fault-once" && !string.IsNullOrEmpty(marker) && !File.Exists(marker))
                    {
                        File.WriteAllText(marker, "First fixture connection intentionally failed.");
                        return 19;
                    }
                    await Reply(new
                    {
                        id,
                        result = new
                        {
                            rateLimits = new
                            {
                                primary = new { usedPercent = 32.0, windowDurationMins = 300, resetsAt = 1999999999L },
                                secondary = new { usedPercent = 58.0, windowDurationMins = 10080, resetsAt = 2000500000L },
                            },
                        },
                    });
                }
                else
                    await Reply(new { id, error = new { code = -32601, message = "Unsupported fixture method." } });
            }
            return 0;
        }
        finally { Log("exited"); }
    }

    private static async Task Reply(object payload)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(payload));
        await Console.Out.FlushAsync();
    }

    private static void Scenario(string mode, Func<FixtureScope, Task> action)
    {
        using var fixture = new FixtureScope(mode);
        var scenario = action(fixture);
        // Test failures remain bounded; cleanup still identifies and terminates
        // only fixture processes whose PID and start time both match our log.
        try { scenario.WaitAsync(TimeSpan.FromSeconds(7)).GetAwaiter().GetResult(); }
        catch
        {
            _ = scenario.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }
    }

    private static string FixtureExecutable()
    {
        var path = Environment.ProcessPath ?? throw new Exception("No fixture executable path.");
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Run the EdgeTests apphost executable to enable fixture integration tests.");
        return path;
    }

    private static void AssertQuota(QuotaData data)
    {
        Equal(32.0, data.Primary?.UsedPercent);
        Equal(300, data.Primary?.WindowMinutes);
        Equal(58.0, data.Secondary?.UsedPercent);
        Equal(10080, data.Secondary?.WindowMinutes);
    }

    private static async Task ExpectCancelled(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { return; }
        throw new Exception("Expected cancellation.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
    }

    private sealed class FixtureScope : IDisposable
    {
        private readonly string _parent;
        private readonly string _directory;
        private readonly string _log;
        private readonly Dictionary<string, string?> _previous = new();

        public FixtureScope(string mode)
        {
            _parent = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            _directory = Path.GetFullPath(Path.Combine(_parent, "client-fixture-" + Guid.NewGuid().ToString("N")));
            _log = Path.Combine(_directory, "protocol.jsonl");
            Directory.CreateDirectory(_directory);
            Set(LogVariable, _log);
            Set(ModeVariable, mode);
            Set(MarkerVariable, Path.Combine(_directory, "fault-once.marker"));
        }

        private void Set(string name, string value)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public FixtureEvent[] ReadEvents()
        {
            if (!File.Exists(_log)) return Array.Empty<FixtureEvent>();
            try
            {
                return File.ReadAllLines(_log).Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => JsonSerializer.Deserialize<FixtureEvent>(line)!).ToArray();
            }
            catch (IOException) { return Array.Empty<FixtureEvent>(); }
            catch (JsonException) { return Array.Empty<FixtureEvent>(); }
        }

        public async Task WaitForMethod(string method)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(3))
            {
                if (ReadEvents().Any(item => item.Method == method)) return;
                await Task.Delay(20);
            }
            throw new TimeoutException("Fixture did not receive " + method + ".");
        }

        public async Task AssertAllChildrenStopped()
        {
            var children = ReadEvents().Where(item => item.Event == "started").ToArray();
            if (children.Length == 0) throw new Exception("Fixture did not record its owned child.");
            var timer = Stopwatch.StartNew();
            while (children.Any(IsSameLiveProcess) && timer.Elapsed < TimeSpan.FromSeconds(1)) await Task.Delay(20);
            if (children.Any(IsSameLiveProcess)) throw new Exception("An owned fixture app-server survived cleanup.");
        }

        private static bool IsSameLiveProcess(FixtureEvent child)
        {
            try
            {
                using var process = Process.GetProcessById(child.Pid);
                return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == child.StartedTicks;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        public void Dispose()
        {
            try
            {
                foreach (var child in ReadEvents().Where(item => item.Event == "started"))
                {
                    if (!IsSameLiveProcess(child)) continue;
                    using var process = Process.GetProcessById(child.Pid);
                    if (process.StartTime.ToUniversalTime().Ticks == child.StartedTicks)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(700);
                    }
                }
            }
            finally
            {
                foreach (var pair in _previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                // Verify the resolved deletion target is a direct fixture child
                // of the test output directory, never a computed parent path.
                var parent = Directory.GetParent(Path.GetFullPath(_directory))?.FullName.TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(parent, _parent, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(_directory).StartsWith("client-fixture-", StringComparison.Ordinal) && Directory.Exists(_directory))
                    Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
