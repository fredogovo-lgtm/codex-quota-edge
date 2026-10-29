// Adapted from Codex Usage Widget's CodexAppServerClient.
// Copyright (c) 2026 九应 (https://github.com/qeretycs-tech)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexEdgeWidget;

/// <summary>
/// Reads quota through an owned Codex app-server. It never opens login files,
/// requests account details, or stores server output or credentials.
/// </summary>
public sealed class QuotaClient(string? executable = null) : IQuotaClient
{
    private readonly string? _configuredExecutable = executable;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly object _disposeLock = new();
    private Process? _process;
    private CancellationTokenSource? _session;
    private Task? _reader;
    private Task? _errorReader;
    private Task? _disposal;
    private long _nextId;
    private int _disposeRequested;
    private bool _initialized;

    public async Task<QuotaData> FetchAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        // This deadline also covers waiting for another refresh and initialize.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await _serial.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await EnsureStartedAsync(deadline.Token).ConfigureAwait(false);
            var result = await RequestAsync("account/rateLimits/read", null, deadline.Token).ConfigureAwait(false);
            return Parse(result);
        }
        catch
        {
            // A broken or cancelled connection is not reused by the next poll.
            await StopProcessAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _serial.Release();
        }
    }

    public static QuotaData Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("rateLimits", out var limits) ||
            limits.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Codex 没有返回可用的额度数据。");

        // Server slot names are not a duration contract. The UI selects periods
        // by WindowMinutes, so a weekly primary window is kept in its true slot.
        return new QuotaData(ParsePeriod(limits, "primary"), ParsePeriod(limits, "secondary"));
    }

    private static QuotaPeriod? ParsePeriod(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var usedValue) ||
            usedValue.ValueKind != JsonValueKind.Number || !usedValue.TryGetDouble(out var used) ||
            !double.IsFinite(used) ||
            !window.TryGetProperty("windowDurationMins", out var durationValue) ||
            durationValue.ValueKind != JsonValueKind.Number || !durationValue.TryGetInt32(out var minutes) || minutes <= 0)
            return null;

        DateTimeOffset? reset = null;
        if (window.TryGetProperty("resetsAt", out var resetValue) &&
            resetValue.ValueKind == JsonValueKind.Number && resetValue.TryGetInt64(out var seconds) &&
            seconds > 0 && seconds <= 253402300799L)
            reset = DateTimeOffset.FromUnixTimeSeconds(seconds);

        return new QuotaPeriod(Math.Clamp(used, 0, 100), minutes, reset);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (IsRunning(_process) && _initialized && _reader is { IsCompleted: false })
            return;

        await StopProcessAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _session = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutable(),
            Arguments = "app-server --stdio",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        // Respect custom CODEX_HOME. Only the CLI accesses its own login state.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_HOME")))
        {
            var defaultHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            if (Directory.Exists(defaultHome)) startInfo.Environment["CODEX_HOME"] = defaultHome;
        }

        try
        {
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Codex CLI 未能启动。");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("无法启动 Codex CLI，请检查 Codex 是否安装完成。", ex);
        }

        _reader = ReadLoopAsync(_process.StandardOutput, _session.Token);
        _errorReader = DrainErrorsAsync(_process.StandardError, _session.Token);
        await RequestAsync("initialize", new
        {
            clientInfo = new { name = "codex-edge-widget", title = "Codex Edge Widget", version = "2.0.0" },
            capabilities = new { },
        }, cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(JsonSerializer.Serialize(new { method = "initialized" }), cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    private async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            var payload = parameters is null
                ? JsonSerializer.Serialize(new { id, method })
                : JsonSerializer.Serialize(new { id, method, @params = parameters });
            await WriteLineAsync(payload, cancellationToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        var process = _process;
        if (!IsRunning(process)) throw new IOException("Codex 额度连接未运行。");
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process!.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                using var document = JsonDocument.Parse(line);
                var response = document.RootElement;
                if (response.ValueKind != JsonValueKind.Object ||
                    !response.TryGetProperty("id", out var idValue) ||
                    idValue.ValueKind != JsonValueKind.Number || !idValue.TryGetInt64(out var id) ||
                    !_pending.TryGetValue(id, out var completion))
                    continue;

                if (response.TryGetProperty("error", out _))
                    completion.TrySetException(new IOException("Codex 额度查询返回错误，请稍后重试。"));
                else if (response.TryGetProperty("result", out var result))
                    completion.TrySetResult(result.Clone());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            FailPending(new IOException("Codex 额度连接中断。"));
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                FailPending(new IOException("Codex 额度连接已停止。"));
        }
    }

    private static async Task DrainErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null) { }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        // Do not retain or print stderr: upstream may include private details.
    }

    private void FailPending(Exception exception)
    {
        foreach (var pending in _pending.Values) pending.TrySetException(exception);
    }

    private async Task StopProcessAsync()
    {
        _initialized = false;
        var process = _process;
        var session = _session;
        var reader = _reader;
        var errorReader = _errorReader;
        _process = null;
        _session = null;
        _reader = null;
        _errorReader = null;
        session?.Cancel();
        FailPending(new OperationCanceledException("额度连接已关闭。"));
        try
        {
            if (process is not null)
            {
                try { process.StandardInput.Close(); }
                catch (InvalidOperationException) { }
                catch (IOException) { }
                if (IsRunning(process))
                {
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false); }
                    catch (TimeoutException)
                    {
                        // This Process is exclusively the child we started, never
                        // the user's Codex desktop or an existing CLI process.
                        if (IsRunning(process)) process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            try
            {
                var drains = new[] { reader, errorReader }.OfType<Task>().ToArray();
                if (drains.Length > 0)
                    await Task.WhenAll(drains).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception) { }
            process?.Dispose();
            session?.Dispose();
        }
    }

    private static bool IsRunning(Process? process)
    {
        if (process is null) return false;
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    private string ResolveExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_configuredExecutable))
        {
            if (File.Exists(_configuredExecutable)) return _configuredExecutable;
            throw new FileNotFoundException("指定的 Codex CLI 不存在。");
        }
        var configured = Environment.GetEnvironmentVariable("CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var local = Path.Combine(AppContext.BaseDirectory, "codex.exe");
        if (File.Exists(local)) return local;

        try
        {
            var bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(bin))
            {
                var candidate = new DirectoryInfo(bin).EnumerateDirectories()
                    .OrderByDescending(directory => directory.LastWriteTimeUtc)
                    .Select(directory => Path.Combine(directory.FullName, "codex.exe")).FirstOrDefault(File.Exists);
                if (candidate is not null) return candidate;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "codex.exe");
        if (File.Exists(alias)) return alias;
        try
        {
            var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (Directory.Exists(windowsApps))
            {
                var candidate = Directory.EnumerateDirectories(windowsApps, "OpenAI.Codex_*__2p2nqsd0c76g0")
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(path => Path.Combine(path, "app", "resources", "codex.exe")).FirstOrDefault(File.Exists);
                if (candidate is not null) return candidate;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return "codex.exe";
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeRequested) != 0) throw new ObjectDisposedException(nameof(QuotaClient));
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposal ??= DisposeCoreAsync();
            return new ValueTask(_disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        _shutdown.Cancel();
        await _serial.WaitAsync().ConfigureAwait(false);
        try { await StopProcessAsync().ConfigureAwait(false); }
        finally
        {
            _serial.Release();
            _shutdown.Dispose();
            // Semaphores deliberately remain managed objects until collection:
            // callers already awaiting the serial gate can finish cancellation
            // without racing a disposed semaphore. No WaitHandle is allocated.
        }
    }
}
