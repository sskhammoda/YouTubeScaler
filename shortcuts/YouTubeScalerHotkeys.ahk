#Requires AutoHotkey v2.0
#SingleInstance Force

; Edit this to point to your built or published YouTubeScaler.exe.
ExePath := "C:\Path\To\YouTubeScaler.exe"

^!y::LaunchScaler(360)
^!4::LaunchScaler(480)

LaunchScaler(quality) {
    savedClipboard := ClipboardAll()
    A_Clipboard := ""
    Send "^l"
    Sleep 80
    Send "^c"
    if !ClipWait(1) {
        A_Clipboard := savedClipboard
        return
    }
    url := Trim(A_Clipboard)
    A_Clipboard := savedClipboard
    if !RegExMatch(url, "i)^https?://(www\.|m\.)?(youtube\.com|youtu\.be)/") {
        MsgBox "The active address is not a YouTube URL.", "YouTubeScaler"
        return
    }
    global ExePath
    if !FileExist(ExePath) {
        MsgBox "Set ExePath in YouTubeScalerHotkeys.ahk first.", "YouTubeScaler"
        return
    }
    Run '"' ExePath '" "' url '" --quality ' quality
}
