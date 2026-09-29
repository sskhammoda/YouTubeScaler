namespace YouTubeScaler;

internal sealed class MainForm : Form
{
    private readonly TextBox _urlBox = new() { Dock = DockStyle.Fill, PlaceholderText = "https://www.youtube.com/watch?v=..." };
    private readonly Button _loadButton = new() { Text = "Load", AutoSize = true };
    private readonly ComboBox _qualityBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
    private readonly ComboBox _audioBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
    private readonly Button _playButton = new() { Text = "Play", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, Text = "Enter a YouTube URL and click Load." };
    private readonly LaunchOptions _options;
    private readonly YoutubeService? _youtube;
    private readonly MpvPlayer? _mpv;
    private CancellationTokenSource? _loadCts;
    private IReadOnlyList<VideoFormat> _formats = [];
    private IReadOnlyList<AudioTrack> _audioTracks = [];
    private VideoFormat? _playingFormat;
    private PlayerForm? _playerForm;
    private bool _populating;

    public MainForm(LaunchOptions options)
    {
        _options = options;
        Text = "YouTubeScaler"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(485, 215);
        var tools = FindTools();
        if (tools.YtDlp is not null) _youtube = new YoutubeService(tools.YtDlp);
        if (tools.Mpv is not null) _mpv = new MpvPlayer(tools.Mpv);
        BuildUi();
        _loadButton.Click += async (_, _) => await LoadAsync(false);
        _playButton.Click += async (_, _) => await StartOrToggleAsync();
        _qualityBox.SelectedIndexChanged += async (_, _) =>
        {
            if (_populating || _qualityBox.SelectedItem is not VideoFormat selected) return;
            if (_playingFormat is not null) await StartPlaybackAsync(true);
            else { _playButton.Enabled = true; _status.Text = StreamStatus(selected); }
        };
        _audioBox.SelectedIndexChanged += async (_, _) =>
        {
            if (_populating || _audioBox.SelectedItem is not AudioTrack) return;
            if (_playingFormat is not null) await StartPlaybackAsync(true);
        };
        Shown += async (_, _) => { if (!string.IsNullOrWhiteSpace(_options.Url)) { _urlBox.Text = _options.Url; await LoadAsync(true); } };
    }

    private void BuildUi()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "YouTube URL:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        var urlRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false }; _urlBox.Width = 315; urlRow.Controls.Add(_urlBox); urlRow.Controls.Add(_loadButton); layout.Controls.Add(urlRow, 1, 0);
        layout.Controls.Add(new Label { Text = "Quality:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1); layout.Controls.Add(_qualityBox, 1, 1);
        layout.Controls.Add(new Label { Text = "Audio:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2); layout.Controls.Add(_audioBox, 1, 2);
        layout.Controls.Add(new Label(), 0, 3); layout.Controls.Add(_playButton, 1, 3);
        layout.Controls.Add(new Label { Text = "Status:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4); layout.Controls.Add(_status, 1, 4);
        Controls.Add(layout);
    }

    private async Task LoadAsync(bool autoPlay)
    {
        if (_youtube is null || _mpv is null) { ShowMissingTools(); return; }
        _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = new CancellationTokenSource();
        try
        {
            if (_mpv.IsRunning) await _mpv.StopAsync();
            _playingFormat = null; _playButton.Text = "Play";
            _populating = true;
            _formats = []; _audioTracks = [];
            _qualityBox.DataSource = null; _audioBox.DataSource = null;
            _playButton.Enabled = false;
            _populating = false;
            SetBusy(true, "Reading available SDR streams…");
            _formats = await _youtube.GetFormatsAsync(_urlBox.Text.Trim(), _loadCts.Token);
            if (_formats.Count == 0) throw new InvalidOperationException("No usable SDR video streams were found.");
            _audioTracks = await _youtube.GetAudioTracksAsync(_urlBox.Text.Trim(), _loadCts.Token);
            _populating = true;
            _qualityBox.DataSource = _formats.ToList(); _qualityBox.DisplayMember = nameof(VideoFormat.DisplayName);
            _audioBox.DataSource = _audioTracks.ToList(); _audioBox.DisplayMember = nameof(AudioTrack.DisplayName);
            _audioBox.SelectedItem = _audioTracks.FirstOrDefault(x => x.IsOriginal) ?? _audioTracks.FirstOrDefault(x => x.IsDefault) ?? _audioTracks.FirstOrDefault();
            _populating = false;
            var target = _options.Quality ?? 360;
            var choice = YoutubeService.SelectAtOrBelow(_formats, target);
            if (choice is null)
            {
                _qualityBox.SelectedIndex = -1; _playButton.Enabled = false;
                _status.Text = $"No {target}p or lower SDR stream. Choose a listed quality manually.";
                return;
            }
            _qualityBox.SelectedItem = choice; _playButton.Enabled = true;
            _status.Text = choice.Resolution == target ? StreamStatus(choice) : $"{target}p unavailable; selected lower {StreamStatus(choice)}";
            if (autoPlay) await StartPlaybackAsync(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _status.Text = ShortError(ex); _playButton.Enabled = false; }
        finally { _populating = false; SetBusy(false, null); }
    }

    private async Task StartOrToggleAsync()
    {
        if (_playingFormat is not null && _mpv?.IsRunning == true) { await _mpv.TogglePauseAsync(); return; }
        await StartPlaybackAsync(false);
    }

    private async Task StartPlaybackAsync(bool preservePosition)
    {
        if (_mpv is null || _youtube is null || _qualityBox.SelectedItem is not VideoFormat selected) return;
        double time = 0; bool paused = false;
        try
        {
            if (preservePosition && _mpv.IsRunning) { time = await _mpv.GetTimeAsync(); paused = await _mpv.IsPausedAsync(); }
            SetBusy(true, "Starting selected stream…");
            // Both URLs originate in the same yt-dlp metadata response. mpv receives no YouTube page URL and cannot select another rendition.
            var audio = (_audioBox.SelectedItem as AudioTrack)?.Format;
            if (audio is null && !selected.HasAudio) throw new InvalidOperationException("No usable audio stream was found.");
            _playerForm ??= CreatePlayerForm();
            if (!_playerForm.Visible) _playerForm.Show(this);
            _playerForm.SetExactPlayerSize(selected.Width, selected.Height);
            await _mpv.StartAsync(_playerForm.Handle, selected, audio, _loadCts?.Token ?? CancellationToken.None);
            _playingFormat = selected;
            if (time > 0) await _mpv.SetTimeAsync(time); if (paused) await _mpv.SetPauseAsync(true);
            _playerForm.Activate();
            var actual = await WaitForVideoDimensionsAsync();
            if (actual is not null && (actual.Value.Width != selected.Width || actual.Value.Height != selected.Height))
            {
                await _mpv.StopAsync(); _playingFormat = null;
                throw new InvalidOperationException($"mpv reported {actual.Value.Width}×{actual.Value.Height}, not requested {selected.Width}×{selected.Height}. Playback was stopped.");
            }
            _playButton.Text = "Play/Pause"; _status.Text = StreamStatus(selected);
        }
        catch (Exception ex) { _playingFormat = null; _status.Text = ShortError(ex); }
        finally { SetBusy(false, null); }
    }

    private async Task<(int Width, int Height)?> WaitForVideoDimensionsAsync()
    {
        if (_mpv is null) return null;
        for (var i = 0; i < 16; i++) { await Task.Delay(250); var dimensions = await _mpv.GetVideoDimensionsAsync(); if (dimensions is not null) return dimensions; }
        return null; // Some codecs report this late; direct URL selection remains authoritative.
    }
    private PlayerForm CreatePlayerForm()
    {
        var form = new PlayerForm(_mpv!);
        form.FormClosed += async (_, _) => { if (_mpv is not null) await _mpv.StopAsync(); _playingFormat = null; _playerForm = null; _playButton.Text = "Play"; };
        return form;
    }
    private void SetBusy(bool busy, string? text)
    {
        _loadButton.Enabled = !busy;
        _qualityBox.Enabled = !busy && _formats.Count > 0;
        _audioBox.Enabled = !busy && _audioTracks.Count > 0;
        if (text is not null) _status.Text = text;
        UseWaitCursor = busy;
    }
    private static string StreamStatus(VideoFormat f) => $"{f.Width}×{f.Height}{Environment.NewLine}{(f.Fps is > 0 ? f.Fps.Value.ToString("0.##") : "?")} FPS{Environment.NewLine}SDR";
    private static string ShortError(Exception ex) => ex.Message.Length > 280 ? ex.Message[..280] : ex.Message;
    protected override void OnFormClosing(FormClosingEventArgs e) { _loadCts?.Cancel(); _youtube?.Dispose(); _mpv?.Dispose(); if (_playerForm is { IsDisposed: false }) _playerForm.Close(); base.OnFormClosing(e); }
    private void ShowMissingTools()
    {
        var missing = new List<string>(); if (_youtube is null) missing.Add("yt-dlp.exe"); if (_mpv is null) missing.Add("mpv.exe");
        _status.Text = "Missing " + string.Join(" and ", missing) + ". Put them in Tools beside YouTubeScaler.exe or add them to PATH.";
    }
    private static (string? Mpv, string? YtDlp) FindTools()
    {
        static string? Find(string name)
        {
            var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "Tools", name), Path.Combine(AppContext.BaseDirectory, name) };
            var local = candidates.FirstOrDefault(File.Exists); if (local is not null) return local;
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) { var path = Path.Combine(folder.Trim(), name); if (File.Exists(path)) return path; }
            return null;
        }
        return (Find("mpv.exe"), Find("yt-dlp.exe"));
    }
}
