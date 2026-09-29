using System.Runtime.InteropServices;

namespace YouTubeScaler;

public sealed class PlayerForm : Form
{
    private readonly MpvPlayer _mpv;
    private bool _spaceDown;
    private bool _controlErrorShown;

    public PlayerForm(MpvPlayer mpv)
    {
        _mpv = mpv;
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false; MaximizeBox = false; MinimizeBox = false;
        BackColor = Color.Black; StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        KeyPreview = true;
        Text = "YouTubeScaler Player";
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = e.KeyCode switch
        {
            Keys.Space => "SPACE", Keys.K => "k", Keys.J => "j", Keys.L => "l",
            Keys.Left => "LEFT", Keys.Right => "RIGHT", Keys.Up => "UP", Keys.Down => "DOWN", Keys.M => "m",
            Keys.D0 or Keys.NumPad0 => "0", Keys.D1 or Keys.NumPad1 => "1", Keys.D2 or Keys.NumPad2 => "2",
            Keys.D3 or Keys.NumPad3 => "3", Keys.D4 or Keys.NumPad4 => "4", Keys.D5 or Keys.NumPad5 => "5",
            Keys.D6 or Keys.NumPad6 => "6", Keys.D7 or Keys.NumPad7 => "7", Keys.D8 or Keys.NumPad8 => "8",
            Keys.D9 or Keys.NumPad9 => "9", _ => null
        };
        if (key is null) { base.OnKeyDown(e); return; }
        e.Handled = true; e.SuppressKeyPress = true;
        if (key == "SPACE")
        {
            if (!_spaceDown) { _spaceDown = true; SendControl(_mpv.KeyDownAsync(key)); }
        }
        else SendControl(_mpv.KeyPressAsync(key));
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _spaceDown)
        {
            _spaceDown = false;
            e.Handled = true;
            SendControl(_mpv.KeyUpAsync("SPACE"));
        }
        else base.OnKeyUp(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (e.KeyChar is '<' or '>') { e.Handled = true; SendControl(_mpv.KeyPressAsync(e.KeyChar.ToString())); }
        else base.OnKeyPress(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        if (_spaceDown) { _spaceDown = false; SendControl(_mpv.KeyUpAsync("SPACE")); }
        base.OnDeactivate(e);
    }

    private async void SendControl(Task command)
    {
        try { await command; }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing || !_mpv.IsRunning || _controlErrorShown) return;
            _controlErrorShown = true;
            MessageBox.Show(this, "Player control failed: " + ex.Message, "YouTubeScaler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // SetWindowPos uses physical screen pixels for a PerMonitorV2 process. A borderless form's window and client rectangles are identical.
    public void SetExactPlayerSize(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!IsHandleCreated) CreateControl();
        SetWindowPos(Handle, IntPtr.Zero, Left, Top, width, height, SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
        GetClientRect(Handle, out var client);
        if (client.Right - client.Left != width || client.Bottom - client.Top != height)
            throw new InvalidOperationException($"Windows did not create the required {width}×{height} pixel video client area.");
    }

    private const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    // Shift+drag is a deliberate, unobtrusive way to move a titleless capture window.
    protected override void WndProc(ref Message m)
    {
        const int WmNcHitTest = 0x84, HtCaption = 2, VkShift = 0x10;
        if (m.Msg == WmNcHitTest && (GetAsyncKeyState(VkShift) & 0x8000) != 0) { m.Result = (IntPtr)HtCaption; return; }
        base.WndProc(ref m);
    }
}
