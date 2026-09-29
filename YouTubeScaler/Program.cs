namespace YouTubeScaler;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Any(x => x.Equals("--self-test", StringComparison.OrdinalIgnoreCase))) { SelfTests.Run(); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(LaunchOptions.Parse(args)));
    }
}

internal sealed record LaunchOptions(string? Url, int? Quality)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? url = args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal));
        int? quality = null;
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--quality", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var q)) quality = q;
        return new(url, quality);
    }
}
