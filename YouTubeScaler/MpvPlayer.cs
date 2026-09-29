using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace YouTubeScaler;

public sealed class MpvPlayer : IDisposable
{
    private readonly string _mpvPath;
    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _requests = [];
    private long _requestId;
    private CancellationTokenSource? _readerCts;
    private bool? _supportsCurlLimits;

    public MpvPlayer(string mpvPath) => _mpvPath = mpvPath;
    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(IntPtr parentWindow, VideoFormat video, VideoFormat? audio, CancellationToken cancellationToken)
    {
        await StopAsync();
        var pipeName = "YouTubeScaler-" + Guid.NewGuid().ToString("N");
        var pipePath = @"\\.\pipe\" + pipeName;
        var psi = new ProcessStartInfo(_mpvPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        psi.ArgumentList.Add("--wid=" + parentWindow.ToInt64());
        psi.ArgumentList.Add("--no-border");
        psi.ArgumentList.Add("--fullscreen=no");
        psi.ArgumentList.Add("--force-window=yes");
        psi.ArgumentList.Add("--ytdl=no");
        psi.ArgumentList.Add("--keepaspect=yes");
        psi.ArgumentList.Add("--video-zoom=0");
        psi.ArgumentList.Add("--hr-seek=yes");
        psi.ArgumentList.Add("--cache=yes");
        psi.ArgumentList.Add("--cache-secs=20");
        psi.ArgumentList.Add("--demuxer-hysteresis-secs=5");
        psi.ArgumentList.Add("--demuxer-max-bytes=4MiB");
        psi.ArgumentList.Add("--demuxer-max-back-bytes=1MiB");
        await AddNetworkOptionsAsync(psi, cancellationToken);
        psi.ArgumentList.Add("--input-ipc-server=" + pipePath);
        psi.ArgumentList.Add("--script=" + Path.Combine(AppContext.BaseDirectory, "mpv-controls.lua"));
        AddHeaders(psi, video.Headers);
        if (audio is not null) psi.ArgumentList.Add("--audio-file=" + audio.StreamUrl);
        psi.ArgumentList.Add(video.StreamUrl); // explicit media URL: mpv cannot select a different video quality.
        cancellationToken.ThrowIfCancellationRequested();
        _process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start mpv.");
        var stderr = _process.StandardError.ReadToEndAsync();
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await _pipe.ConnectAsync(5000, cancellationToken); }
        catch
        {
            var error = _process.HasExited ? await stderr : string.Empty;
            await StopAsync();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "mpv started but its control pipe was unavailable."
                : "mpv could not open the selected stream: " + ReadableMpvError(error));
        }
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        _readerCts = new CancellationTokenSource();
        _ = ReadLoopAsync(_readerCts.Token);
    }

    private async Task AddNetworkOptionsAsync(ProcessStartInfo playback, CancellationToken cancellationToken)
    {
        // Direct URLs skip mpv's yt-dlp hook, including its HTTP chunk limit. Unbounded YouTube requests can be throttled.
        // Use 1 MiB ranges for the same selected streams, keeping the normal 20-second demuxer cache.
        playback.ArgumentList.Add("--stream-lavf-o=request_size=1048576,multiple_requests=1,short_seek_size=1048576");
        if (_supportsCurlLimits is null)
        {
            // New mpv builds use curl instead of FFmpeg for HTTP. Do not pass unknown curl options to older builds.
            var info = new ProcessStartInfo(_mpvPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("--list-options");
            using var probe = Process.Start(info) ?? throw new InvalidOperationException("Could not inspect mpv options.");
            var output = probe.StandardOutput.ReadToEndAsync();
            var error = probe.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { await probe.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!probe.HasExited) probe.Kill(entireProcessTree: true); throw; }
            await error;
            if (probe.ExitCode != 0) throw new InvalidOperationException("Could not inspect mpv options. Check your mpv installation.");
            var options = await output;
            _supportsCurlLimits = options.Contains("--curl-max-request-size", StringComparison.Ordinal) && options.Contains("--curl-buffer-size", StringComparison.Ordinal);
        }
        if (_supportsCurlLimits == true)
        {
            playback.ArgumentList.Add("--curl-max-request-size=1MiB");
            playback.ArgumentList.Add("--curl-buffer-size=256KiB");
        }
    }

    public async Task<bool> IsPausedAsync() => await GetPropertyAsync<bool>("pause");
    public async Task<double> GetTimeAsync() => await GetPropertyAsync<double>("time-pos");
    public async Task<double> GetSpeedAsync() => await GetPropertyAsync<double>("speed");
    public async Task<double> GetDurationAsync() => await GetPropertyAsync<double>("duration");
    public async Task<(int Width, int Height)?> GetVideoDimensionsAsync()
    {
        var value = await GetPropertyAsync<JsonElement>("video-params");
        if (value.ValueKind != JsonValueKind.Object) return null;
        var w = ReadInt(value, "w"); var h = ReadInt(value, "h");
        return w > 0 && h > 0 ? (w, h) : null;
    }
    public Task TogglePauseAsync() => CommandAsync("cycle", "pause");
    public Task SeekAsync(double seconds) => CommandAsync("seek", seconds, "relative");
    public Task SetTimeAsync(double seconds) => CommandAsync("seek", seconds, "absolute", "exact");
    public Task SetPauseAsync(bool pause) => CommandAsync("set_property", "pause", pause);
    public Task SetSpeedAsync(double speed) => CommandAsync("set_property", "speed", speed);
    public Task KeyPressAsync(string key) => CommandAsync("keypress", key);
    public Task KeyDownAsync(string key) => CommandAsync("keydown", key);
    public Task KeyUpAsync(string key) => CommandAsync("keyup", key);

    private async Task<T> GetPropertyAsync<T>(string property)
    {
        var response = await CommandWithResponseAsync("get_property", property);
        if (!response.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null) return default!;
        try { return data.Deserialize<T>()!; } catch { return default!; }
    }
    private async Task CommandAsync(params object[] command) => await SendAsync(command, false);
    private Task<JsonElement> CommandWithResponseAsync(params object[] command) => SendAsync(command, true);
    private async Task<JsonElement> SendAsync(object[] command, bool response)
    {
        if (_writer is null || !IsRunning) throw new InvalidOperationException("mpv is not running.");
        var id = Interlocked.Increment(ref _requestId);
        TaskCompletionSource<JsonElement>? completion = null;
        if (response) { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); lock (_requests) _requests[id] = completion; }
        var message = JsonSerializer.Serialize(new { command, request_id = id });
        await _writeLock.WaitAsync();
        try { await _writer.WriteLineAsync(message); }
        finally { _writeLock.Release(); }
        return completion is null ? default : await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(_pipe!, Encoding.UTF8, false, 1024, leaveOpen: true);
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken); if (line is null) break;
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                if (root.TryGetProperty("request_id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out var id))
                {
                    TaskCompletionSource<JsonElement>? tcs; lock (_requests) { _requests.Remove(id, out tcs); }
                    tcs?.TrySetResult(root.Clone());
                }
            }
        }
        catch { }
        finally { lock (_requests) foreach (var request in _requests.Values) request.TrySetException(new InvalidOperationException("mpv control connection closed.")); _requests.Clear(); }
    }
    private static void AddHeaders(ProcessStartInfo psi, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers.Where(x => x.Key is "User-Agent" or "Referer" or "Origin")) psi.ArgumentList.Add("--http-header-fields=" + name + ": " + value);
    }
    private static int ReadInt(JsonElement element, string name) => element.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var value) ? value : 0;
    private static string ReadableMpvError(string error) => error.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "unknown mpv error";
    public async Task StopAsync()
    {
        var process = _process;
        try { if (_writer is not null && process is { HasExited: false }) await CommandAsync("quit"); } catch { }
        _process = null;
        _readerCts?.Cancel(); _readerCts?.Dispose(); _readerCts = null;
        _writer?.Dispose(); _writer = null; _pipe?.Dispose(); _pipe = null;
        if (process is not null) { try { if (!process.HasExited && !process.WaitForExit(1200)) process.Kill(entireProcessTree: true); } catch { } process.Dispose(); }
    }
    // Form shutdown runs on the UI thread. Run async pipe cleanup off that thread so its continuations cannot deadlock shutdown.
    public void Dispose() { Task.Run(StopAsync).GetAwaiter().GetResult(); _writeLock.Dispose(); }
}
