using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace YouTubeScaler;

public sealed class YoutubeService : IDisposable
{
    private Process? _activeProcess;
    private readonly string _ytDlpPath;
    private string? _metadataUrl;
    private IReadOnlyList<VideoFormat> _metadataFormats = [];

    public YoutubeService(string ytDlpPath) => _ytDlpPath = ytDlpPath;

    public static bool IsYoutubeUrl(string? text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host is "youtu.be" or "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com";
    }

    public async Task<IReadOnlyList<VideoFormat>> GetFormatsAsync(string url, CancellationToken cancellationToken)
    {
        if (!IsYoutubeUrl(url)) throw new InvalidOperationException("Enter a valid YouTube video URL.");
        var psi = new ProcessStartInfo(_ytDlpPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.ArgumentList.Add("--dump-single-json");
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--skip-download");
        psi.ArgumentList.Add(url);
        using var process = new Process { StartInfo = psi };
        _activeProcess = process;
        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            var json = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException(ReadableYtDlpError(error));
            _metadataUrl = url;
            _metadataFormats = ParseAllFormats(json);
            return SelectVideoFormats(_metadataFormats);
        }
        catch (OperationCanceledException) { StopProcess(process); throw; }
        finally { if (ReferenceEquals(_activeProcess, process)) _activeProcess = null; }
    }

    public static IReadOnlyList<VideoFormat> ParseFormats(string json)
    {
        return SelectVideoFormats(ParseAllFormats(json));
    }

    private static IReadOnlyList<VideoFormat> SelectVideoFormats(IEnumerable<VideoFormat> formats)
    {
        var all = formats.Where(x => x.Width > 0 && x.Height > 0 && !string.IsNullOrWhiteSpace(x.StreamUrl) &&
            !string.IsNullOrWhiteSpace(x.VideoCodec) && !x.VideoCodec.Equals("none", StringComparison.OrdinalIgnoreCase) && x.IsSdr).ToList();
        // One user-facing option per actual dimension. The selected stream remains exactly one of the listed formats.
        return all.GroupBy(x => (x.Width, x.Height))
            .Select(g => g.OrderBy(FpsRank).ThenBy(x => x.HasAudio ? 1 : 0).ThenBy(x => CodecRank(x.VideoCodec)).ThenBy(x => x.BitrateKbps ?? double.MaxValue).First())
            .OrderBy(x => x.Resolution).ThenBy(x => x.Width).ToList();
    }

    public async Task<IReadOnlyList<AudioTrack>> GetAudioTracksAsync(string url, CancellationToken cancellationToken)
    {
        // Reuse the metadata that supplied the selected video URL; never ask mpv/yt-dlp to choose "best".
        var formats = string.Equals(_metadataUrl, url, StringComparison.Ordinal) ? _metadataFormats : await GetRawFormatsAsync(url, cancellationToken);
        return BuildAudioTracks(formats);
    }

    public static IReadOnlyList<AudioTrack> ParseAudioTracks(string json) => BuildAudioTracks(ParseAllFormats(json));

    public static VideoFormat? SelectAtOrBelow(IEnumerable<VideoFormat> formats, int requestedResolution) =>
        formats.Where(x => x.Resolution <= requestedResolution).OrderByDescending(x => x.Resolution).FirstOrDefault();

    private async Task<List<VideoFormat>> GetRawFormatsAsync(string url, CancellationToken cancellationToken)
    {
        // Metadata has already been obtained by the caller in normal use. This intentionally performs a fresh, explicit metadata query
        // only when a video-only stream needs an audio URL; neither request downloads media.
        var psi = new ProcessStartInfo(_ytDlpPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.ArgumentList.Add("--dump-single-json"); psi.ArgumentList.Add("--no-playlist"); psi.ArgumentList.Add("--skip-download"); psi.ArgumentList.Add(url);
        using var process = new Process { StartInfo = psi }; _activeProcess = process; process.Start();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { StopProcess(process); throw; }
        finally { if (ReferenceEquals(_activeProcess, process)) _activeProcess = null; }
        if (process.ExitCode != 0) throw new InvalidOperationException(ReadableYtDlpError(await error));
        return ParseAllFormats(await output);
    }

    private static List<VideoFormat> ParseAllFormats(string json)
    {
        using var document = JsonDocument.Parse(json); var result = new List<VideoFormat>();
        if (!document.RootElement.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in formats.EnumerateArray())
        {
            var url = GetString(item, "url"); if (string.IsNullOrEmpty(url)) continue;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("http_headers", out var h) && h.ValueKind == JsonValueKind.Object) foreach (var p in h.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.String) headers[p.Name] = p.Value.GetString()!;
            var hasDrm = item.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True;
            if (hasDrm) continue;
            result.Add(new VideoFormat(
                GetString(item, "format_id"), GetNullableInt(item, "width").GetValueOrDefault(), GetNullableInt(item, "height").GetValueOrDefault(),
                GetNullableDouble(item, "fps"), GetString(item, "vcodec"), GetString(item, "acodec"), GetString(item, "dynamic_range"),
                GetNullableDouble(item, "abr") ?? GetNullableDouble(item, "tbr"), url, headers,
                GetString(item, "language"), GetString(item, "format_note"), GetNullableInt(item, "language_preference"), GetNullableInt(item, "audio_channels")));
        }
        return result;
    }
    private static IReadOnlyList<AudioTrack> BuildAudioTracks(IEnumerable<VideoFormat> formats) => formats
        // Only separately addressable audio-only formats are safe to pair with the explicit low-resolution video URL.
        .Where(x => x.VideoCodec.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(x.AudioCodec) && !x.AudioCodec.Equals("none", StringComparison.OrdinalIgnoreCase))
        .GroupBy(AudioGroupKey)
        .Select(group =>
        {
            var candidates = group.ToList();
            var isOriginal = candidates.Any(IsOriginalAudio);
            var isDefault = candidates.Any(IsDefaultAudio);
            var chosen = candidates.OrderBy(AudioCodecRank).ThenBy(x => x.BitrateKbps ?? double.MaxValue).First();
            return new AudioTrack(chosen, AudioLabel(chosen, isOriginal), isOriginal, isDefault);
        })
        .OrderByDescending(x => x.IsOriginal).ThenByDescending(x => x.IsDefault).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
    private static string AudioGroupKey(VideoFormat format) => !string.IsNullOrWhiteSpace(format.Language)
        ? "language:" + format.Language.ToLowerInvariant()
        : !string.IsNullOrWhiteSpace(format.FormatNote) ? "note:" + format.FormatNote.ToLowerInvariant() : "format:" + format.FormatId;
    private static int AudioCodecRank(VideoFormat format) => format.AudioCodec.StartsWith("opus", StringComparison.OrdinalIgnoreCase) ? 0 : format.AudioCodec.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    private static bool IsOriginalAudio(VideoFormat format) => format.FormatNote.Contains("original", StringComparison.OrdinalIgnoreCase);
    private static bool IsDefaultAudio(VideoFormat format) => IsOriginalAudio(format) || format.FormatNote.Contains("default", StringComparison.OrdinalIgnoreCase) || format.LanguagePreference is > 0;
    private static string AudioLabel(VideoFormat format, bool isOriginal)
    {
        var language = LanguageLabel(format.Language);
        if (isOriginal) return string.IsNullOrEmpty(language) ? "Original audio" : $"Original ({language})";
        if (!string.IsNullOrEmpty(language)) return language;
        if (!string.IsNullOrWhiteSpace(format.FormatNote)) return format.FormatNote;
        return $"Audio track ({format.FormatId})";
    }
    private static string LanguageLabel(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "";
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            return CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName).EnglishName;
        }
        catch (CultureNotFoundException) { return language; }
    }
    public void Cancel() { try { if (_activeProcess is { HasExited: false }) _activeProcess.Kill(entireProcessTree: true); } catch { } }
    public void Dispose() => Cancel();
    private static int CodecRank(string codec) => codec.StartsWith("av01", StringComparison.OrdinalIgnoreCase) ? 0 : codec.StartsWith("vp9", StringComparison.OrdinalIgnoreCase) ? 1 : codec.StartsWith("avc", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    private static int FpsRank(VideoFormat format) => format.Fps switch { <= 30.01 => 0, null => 1, _ => 2 };
    private static void StopProcess(Process process) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } }
    // Do not guess when metadata omits the range: this utility promises SDR-only playback.
    private static string GetString(JsonElement e, string name) => e.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "";
    private static int? GetNullableInt(JsonElement e, string name) => e.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var n) ? n : null;
    private static double? GetNullableDouble(JsonElement e, string name) => e.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetDouble(out var n) ? n : null;
    private static string ReadableYtDlpError(string error) => string.IsNullOrWhiteSpace(error) ? "yt-dlp could not read this video." : "yt-dlp: " + error.Trim().Split('\n').Last();
}
