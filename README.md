# YouTubeScaler

> **Proof of concept / experimental project**

YouTubeScaler is a small Windows application I made to experiment with playing lower-resolution YouTube videos and upscaling them locally with Lossless Scaling.

The idea is to stream a lower resolution, such as 360p or 480p, through mpv and then use local upscaling to improve how it looks on a larger display. The goal was to test whether this could provide a reasonable viewing experience while using less internet data.

This is a personal learning project and proof of concept, not a polished production application. Some features may be incomplete or unreliable.

## How it works

YouTubeScaler uses:

- **C# / .NET 8** for the application
- **yt-dlp** to find available YouTube video and audio streams
- **mpv** for video playback
- **Lossless Scaling** separately for local upscaling

The user pastes a YouTube URL, loads the available qualities, chooses a resolution and audio track, and opens the selected stream in a separate player window.

The player window is sized to the actual resolution of the selected video so it can then be upscaled using Lossless Scaling.

YouTubeScaler focuses mainly on lower resolutions such as **360p and 480p**.

## Features

- Load a YouTube video from its URL
- Show available video resolutions
- Select an audio track when multiple tracks are available
- Avoid HDR formats
- Prefer SDR formats at 30 FPS or lower when appropriate
- Play the selected stream using mpv
- Switch video quality while keeping the current playback position
- Switch audio tracks
- Restore play/pause state after changing streams
- Open the player at the selected video's actual resolution
- Basic keyboard playback controls
- Optional AutoHotkey shortcuts for quickly opening a YouTube video
- Command-line quality selection

## Requirements

- Windows
- .NET 8 Desktop Runtime
- `mpv.exe`
- `yt-dlp.exe`
- Lossless Scaling if you want to use the local upscaling workflow

`mpv.exe` and `yt-dlp.exe` can be placed in the `Tools` folder beside the application. The app can also find them beside the executable or through `PATH`.

Lossless Scaling is a separate application and is not included or controlled by YouTubeScaler.

## Build

Clone the repository and build the solution with .NET 8:

```powershell
dotnet build YouTubeScaler.sln -c Release
```

The built application will normally be located at:

```text
YouTubeScaler\bin\Release\net8.0-windows\YouTubeScaler.exe
```

You will need to provide `mpv.exe` and `yt-dlp.exe` separately.

## Usage

1. Open YouTubeScaler.
2. Paste a YouTube video URL.
3. Click **Load**.
4. Choose the video quality you want.
5. Choose an audio track if needed.
6. Click **Play**.
7. The video opens in a separate player window.
8. Use Lossless Scaling on the player window if you want to upscale it.

The default target is 360p.

If the requested resolution isn't available, the app tries to select the closest lower resolution instead of silently using a higher one.

## Player controls

With the video player focused:

| Key | Action |
| --- | --- |
| Space | Play/pause |
| Hold Space | Temporary 2× playback speed |
| J / L | Back/forward 10 seconds |
| Left / Right | Back/forward 5 seconds |
| Up / Down | Volume |
| M | Mute |
| 0 | Go to the beginning |
| 1–9 | Jump to 10%–90% of the video |
| < / > | Decrease/increase playback speed |

The borderless player can also be moved by holding **Shift** while dragging it.

## Browser shortcuts

An optional AutoHotkey v2 script is included in the `shortcuts` folder.

After setting `ExePath` in the script to the location of your built YouTubeScaler executable:

- `Ctrl + Alt + Y` opens the current YouTube URL targeting 360p.
- `Ctrl + Alt + 4` opens it targeting 480p.

The script gets the URL from the browser address bar and passes it to YouTubeScaler.

## Command line

YouTubeScaler can also be launched with a URL directly:

```powershell
YouTubeScaler.exe "https://www.youtube.com/watch?v=VIDEO_ID"
```

A target quality can also be specified:

```powershell
YouTubeScaler.exe "https://www.youtube.com/watch?v=VIDEO_ID" --quality 360
```

or:

```powershell
YouTubeScaler.exe "https://www.youtube.com/watch?v=VIDEO_ID" --quality 480
```

## Why I made it

I wanted to experiment with whether lower-resolution video could be streamed using less data and then improved locally using real-time upscaling.

Instead of trying to make a complete replacement for YouTube's player, this project focuses on testing that specific idea.

It also gave me experience working with C#/.NET, external command-line tools, video playback, process control, stream selection, and a Windows desktop UI.

## Limitations

This is an experimental proof of concept, so it has some limitations:

- It depends on yt-dlp and YouTube's current format behavior.
- YouTube changes can temporarily break functionality.
- Some videos or formats may not work correctly.
- It does not bypass account, age, regional, private-video, or DRM restrictions.
- Lossless Scaling must be installed and used separately.
- The interface and error handling are still basic.
- It has not been extensively tested across different systems and configurations.

Keeping `yt-dlp` and `mpv` up to date is recommended.

## Project status

**Experimental / proof of concept**

The main idea works, but the project is not intended to be a polished or production-ready YouTube client. I may continue improving it as I learn more.