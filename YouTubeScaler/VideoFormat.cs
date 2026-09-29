namespace YouTubeScaler;

public sealed record VideoFormat(
    string FormatId, int Width, int Height, double? Fps, string VideoCodec, string AudioCodec,
    string DynamicRange, double? BitrateKbps, string StreamUrl, IReadOnlyDictionary<string, string> Headers,
    string Language = "", string FormatNote = "", int? LanguagePreference = null, int? AudioChannels = null)
{
    public int Resolution => Math.Min(Width, Height);
    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioCodec) && !AudioCodec.Equals("none", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => $"{Resolution}p - {Width}×{Height}";
    public bool IsSdr => DynamicRange.Equals("SDR", StringComparison.OrdinalIgnoreCase);
}

public sealed record AudioTrack(VideoFormat Format, string DisplayName, bool IsOriginal, bool IsDefault)
{
    public string FormatId => Format.FormatId;
}
