namespace YouTubeScaler;

// Dependency-free checks for selection and argument logic. Run: YouTubeScaler.exe --self-test
internal static class SelfTests
{
    public static void Run()
    {
        const string metadata = """
        {"formats":[
          {"format_id":"360-60","width":640,"height":360,"fps":60,"vcodec":"vp09","acodec":"none","dynamic_range":"SDR","url":"https://example/video60"},
          {"format_id":"360-30","width":640,"height":360,"fps":30,"tbr":null,"vcodec":"avc1","acodec":"none","dynamic_range":"SDR","url":"https://example/video30"},
          {"format_id":"480","width":854,"height":480,"fps":null,"tbr":null,"vcodec":"avc1","acodec":"none","dynamic_range":"SDR","url":"https://example/video480"},
          {"format_id":"hdr","width":1280,"height":720,"fps":30,"vcodec":"vp09","acodec":"none","dynamic_range":"HDR","url":"https://example/hdr"},
          {"format_id":"drm","width":640,"height":360,"fps":30,"vcodec":"avc1","acodec":"none","dynamic_range":"SDR","has_drm":true,"url":"https://example/drm"},
          {"format_id":"audio-null","width":null,"height":null,"fps":null,"tbr":null,"vcodec":"none","acodec":"opus","dynamic_range":null,"format_note":null,"has_drm":null,"url":"https://example/audio"},
          {"format_id":"249-en","width":null,"height":null,"vcodec":"none","acodec":"opus","abr":48,"language":"en-US","format_note":"English (US), low","audio_channels":2,"url":"https://example/audio-en-low"},
          {"format_id":"251-en","width":null,"height":null,"vcodec":"none","acodec":"opus","abr":128,"language":"en-US","format_note":"English (US), medium","audio_channels":2,"url":"https://example/audio-en-medium"},
          {"format_id":"249-ar","width":null,"height":null,"vcodec":"none","acodec":"opus","abr":48,"language":"ar","language_preference":10,"format_note":"Arabic original (default), low","audio_channels":2,"url":"https://example/audio-ar"},
          {"format_id":"249-es","width":null,"height":null,"vcodec":"none","acodec":"opus","abr":48,"language":"es","format_note":"Spanish, low","url":"https://example/audio-es"},
          {"format_id":"missing-optional","width":320,"height":180,"vcodec":"avc1","dynamic_range":"SDR","url":"https://example/video180"}
        ]}
        """;
        var formats = YoutubeService.ParseFormats(metadata);
        Assert(formats.Count == 3, "HDR, DRM, and audio-only formats must not enter video quality choices.");
        Assert(formats.Single(x => x.Height == 360).FormatId == "360-30", "30 FPS must beat 60 FPS at equal size.");
        Assert(formats.Single(x => x.Height == 480).Width == 854, "Actual metadata dimensions must be preserved.");
        Assert(formats.Single(x => x.Height == 480).Fps is null && formats.Single(x => x.Height == 480).BitrateKbps is null, "Null FPS/bitrate metadata must remain unknown without failing discovery.");
        Assert(formats.Single(x => x.FormatId == "missing-optional").Fps is null, "Missing optional metadata must not abort discovery.");
        Assert(YoutubeService.SelectAtOrBelow(formats, 360)?.FormatId == "360-30", "360p must select the actual 360p stream.");
        Assert(YoutubeService.SelectAtOrBelow(formats, 400)?.FormatId == "360-30", "Selection must not fall upward.");
        Assert(YoutubeService.SelectAtOrBelow(formats.Where(x => x.Resolution < 480), 480)?.FormatId == "360-30", "Missing requested quality must fall lower.");
        var no360 = formats.Where(x => x.Resolution != 360).Append(new VideoFormat("240", 426, 240, 30, "avc1", "none", "SDR", 200, "https://example/video240", new Dictionary<string, string>()));
        Assert(YoutubeService.SelectAtOrBelow(no360, 360)?.FormatId == "240", "360p must fall to 240p when 360p is unavailable.");
        var audioTracks = YoutubeService.ParseAudioTracks(metadata);
        Assert(audioTracks.Count == 4, "Audio tracks must be grouped by actual language and retain unknown metadata safely.");
        var original = audioTracks.Single(x => x.IsOriginal);
        Assert(original.FormatId == "249-ar" && original.DisplayName == "Original (Arabic)", "The confidently marked original audio must be selected and labelled.");
        var english = audioTracks.Single(x => x.DisplayName == "English");
        Assert(english.FormatId == "249-en", "Duplicate formats for one language must choose the lower-data usable audio stream.");
        Assert(audioTracks.Any(x => x.FormatId == "audio-null"), "Null or missing audio language metadata must not abort discovery.");
        var video360 = YoutubeService.SelectAtOrBelow(formats, 360)!;
        var selectedDub = audioTracks.Single(x => x.FormatId == "249-es");
        Assert(video360.FormatId == "360-30" && selectedDub.FormatId == "249-es", "Changing audio must leave the selected 360p video format unchanged.");
        var video480 = YoutubeService.SelectAtOrBelow(formats, 480)!;
        Assert(video480.FormatId == "480" && selectedDub.FormatId == "249-es", "Changing quality must retain the selected audio format independently.");
        const string newVideoMetadata = """{"formats":[{"format_id":"140-new","vcodec":"none","acodec":"mp4a.40.2","language":"fr","format_note":"French, low","url":"https://example/new-audio"}]}""";
        var newVideoAudio = YoutubeService.ParseAudioTracks(newVideoMetadata);
        Assert(newVideoAudio.Count == 1 && newVideoAudio[0].FormatId == "140-new" && newVideoAudio[0].DisplayName == "French", "Loading another video must use its fresh audio list rather than a prior format ID.");
        Assert(YoutubeService.IsYoutubeUrl("https://youtu.be/example"), "Short YouTube URLs must be accepted.");
        Assert(!YoutubeService.IsYoutubeUrl("https://example.com/watch?v=x"), "Non-YouTube URLs must be rejected.");
        var command = LaunchOptions.Parse(["https://youtu.be/example", "--quality", "480"]);
        Assert(command.Quality == 480 && command.Url is not null, "Command-line quality parsing failed.");
        Console.WriteLine("YouTubeScaler self-tests passed.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("Self-test failed: " + message); }
}
