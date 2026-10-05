using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Tyler;

// =========================================================================
// TylerGui
// --------
// A raw Win32 + DWM + GDI native window: dark themed, rounded, DPI aware.
// No WinForms / WPF / Avalonia / third-party UI. Every native call goes
// through [LibraryImport] source-generated P/Invoke so the Native AOT /
// trimming guarantees are preserved (no reflection, no [DllImport]).
// =========================================================================
internal static partial class TylerGui
{
    internal const int ExitSuccess = 0;
    internal const int ExitUnexpected = 1;

    // -----------------------------------------------------------------
    // Window messages
    // -----------------------------------------------------------------
    private const uint WmCreate = 0x0001;
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmSetCursor = 0x0020;
    private const uint WmCommand = 0x0111;
    private const uint WmSize = 0x0005;
    private const uint WmDropFiles = 0x0233;
    private const uint WmGetDlgCode = 0x0087;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmSetIcon = 0x0080;
    private const nuint IconBig = 1;    // ICON_BIG   (large icon: 32x32)
    private const nuint IconSmall = 0;  // ICON_SMALL (small icon: 16x16)
    private const uint WmCtlColorEdit = 0x0133;
    private const uint WmCtlColorListbox = 0x0134;
    private const uint WmCtlColorStatic = 0x0138;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmMouseLeave = 0x02A3;
    private const uint WmApp = 0x8000;
    private const uint WmQuit = 0x0012;

    // -----------------------------------------------------------------
    // Window / extended / class styles
    // -----------------------------------------------------------------
    private const uint WsOverlapped = 0x00000000;
    private const uint WsCaption = 0x00C00000;
    private const uint WsSysMenu = 0x00080000;
    private const uint WsThickFrame = 0x00040000;
    private const uint WsMinimizeBox = 0x00020000;
    private const uint WsMaximizeBox = 0x00010000;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsTabstop = 0x00010000;
    private const uint WsBorder = 0x00800000;
    private const uint WsVscroll = 0x00200000;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsOverlappedWindow = WsOverlapped | WsCaption | WsSysMenu | WsThickFrame | WsMinimizeBox | WsMaximizeBox;
    private const uint WsExAppWindow = 0x00040000;
    private const uint EsAutohscroll = 0x0080;
    private const uint CbsDropdownList = 0x0003;
    private const uint CsHRedraw = 0x0002;
    private const uint CsVRedraw = 0x0001;

    // -----------------------------------------------------------------
    // Control / command ids
    // -----------------------------------------------------------------
    private const int CmdInputBrowse = 1;
    private const int CmdOutputBrowse = 2;
    private const int CmdConvert = 3;
    private const int CtlInputEdit = 100;
    private const int CtlOutputEdit = 101;
    private const int CtlPaperCombo = 102;
    private const int CtlOrientationCombo = 103;
    private const int CtlColumnsCombo = 104;
    private const int CtlMarginEdit = 105;
    private const int CtlOverlapEdit = 106;

    // -----------------------------------------------------------------
    // Common control messages
    // -----------------------------------------------------------------
    private const uint EmSetReadOnly = 0x00CF;
    private const uint CbAddString = 0x0143;
    private const uint CbSetCurSel = 0x014E;
    private const uint CbGetCurSel = 0x0147;
    private const uint CbGetLbTextLen = 0x0149;
    private const uint CbGetLbText = 0x0148;

    // -----------------------------------------------------------------
    // DrawText / stock objects / DIB / misc constants
    // -----------------------------------------------------------------
    private const uint DtSingleLine = 0x00000020;
    private const uint DtCenter = 0x00000001;
    private const uint DtVcenter = 0x00000004;
    private const uint DtLeft = 0x00000000;
    private const uint DtTop = 0x00000000;
    private const uint DtNoPrefix = 0x00000800;
    private const int DefaultGuiFont = 17;
    private const int StockNullPen = 8;
    private const int StockNullBrush = 5;
    private const uint PmRemove = 0x0001;
    private const uint SrcCopy = 0x00CC0020;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private const uint PwRenderFullContent = 0x00000002;
    private const uint PwClientOnly = 0x00000001;
    private const uint MbOk = 0x00000000;
    private const uint MbIconInformation = 0x00000040;
    private const uint MbIconError = 0x00000010;
    private const uint OfnExplorer = 0x00080000;
    private const uint OfnPathMustExist = 0x00000800;
    private const uint OfnFileMustExist = 0x00001000;
    private const uint OfnOverwriteprompt = 0x00000002;
    private const uint TmeLeave = 0x00000002;
    private const int GwlpUserdata = -21;
    private const int SmCMonitors = 80;
    private const uint DwmwaUseImmersiveDarkMode = 20;
    private const uint DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const uint DwmwaWindowCornerPreference = 33;
    // DWMWA_CAPTION_COLOR. (The canonical value in dwmapi.h is 35; it is used
    // only to tint the title bar dark and fails silently on older systems.)
    private const uint DwmwaCaptionColor = 35;
    private const int DwmcpRound = 2;
    private static readonly nint DpiAwarenessPerMonitorV2 = new(-4);
    private static readonly nint IdcHand = new(32649); // MAKEINTRESOURCE(IDC_HAND)

    private static readonly string MainWindowClass = "TylerMainWindow";
    private static readonly string ButtonClass = "TylerButton";

    // Icon image type + load flags for LoadImageW.
    private const uint ImageIcon = 1;       // IMAGE_ICON
    private const uint LrLoadFromFile = 0x10; // LR_LOADFROMFILE

    // -----------------------------------------------------------------
    // Managed window state (single top-level window per process)
    // -----------------------------------------------------------------
    private static nint _hwnd;
    private static nint _hInstance;
    private static nint _hInputEdit;
    private static nint _hOutputEdit;
    private static nint _hPaperCombo;
    private static nint _hOrientationCombo;
    private static nint _hColumnsCombo;
    private static nint _hMarginEdit;
    private static nint _hOverlapEdit;
    private static nint _hConvertButton;
    private static nint _hBrowseInButton;
    private static nint _hBrowseOutButton;

    private static nint _uiFont;
    private static nint _titleFont;
    private static nint _bgBrush;
    private static nint _panelBrush;
    private static nint _accentBrush;
    private static nint _accentHoverBrush;
    private static nint _accentPressedBrush;
    private static nint _editBrush;

    // Loaded Tyler window icons (small 16 / big 32). Nulled once destroyed.
    private static nint _hIconSmall;
    private static nint _hIconBig;

    private static float _scale = 1.0f;
    private static int _exitCode;

    // Result marshalling for the background Convert task.
    private static int _bgExitCode;
    private static string? _bgMessage;

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    // ==== HEADLESS DETECTION ====
    /// <summary>
    /// Returns true only when a real interactive GUI can be shown: Windows,
    /// a user-interactive session, and at least one attached monitor.
    /// </summary>
    internal static bool IsGuiAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!Environment.UserInteractive)
        {
            return false;
        }

        try
        {
            if (GetSystemMetrics(SmCMonitors) <= 0)
            {
                // Session 0 / service / no display.
                return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    // ==== HIDDEN DOCUMENTATION FLAG: --gui-screenshot <output.png> ====
    internal static bool TryGetScreenshotPath(string[] args, out string? path)
    {
        path = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--gui-screenshot", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    path = args[i + 1];
                    return true;
                }
            }
        }

        return false;
    }

    internal static int RunScreenshot(string outputPath)
    {
        if (!IsGuiAvailable())
        {
            Console.Error.WriteLine("ERROR: --gui-screenshot requires an interactive display (no monitor detected).");
            return ExitUnexpected;
        }

        return RunWindowCore(outputPath);
    }

    internal static int RunGui() => RunWindowCore(null);

    // ==== WINDOW CLASS REGISTRATION + CREATION ====
    private static int RunWindowCore(string? screenshotPath)
    {
        _exitCode = ExitSuccess;

        try
        {
            TrySetProcessDpiAwareness();

            _hInstance = GetModuleHandleW(null);
            if (_hInstance == 0)
            {
                return ExitUnexpected;
            }

            RegisterWindowClass();
            CreateControls(screenshotPath);

            if (_hwnd == 0)
            {
                return ExitUnexpected;
            }

            ApplyLayout();

            ShowWindow(_hwnd, 5 /* SW_SHOW */);
            UpdateWindow(_hwnd);

            // Message loop. For the hidden screenshot flag we exit as soon as
            // the first paint completes; for the interactive GUI we run until
            // WM_QUIT.
            _messageLoopRunning = true;
            while (_messageLoopRunning)
            {
                if (!GetMessageW(out MSG msg, 0, 0, 0))
                {
                    _exitCode = (int)msg.wParam;
                    break;
                }

                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);

                if (screenshotPath is not null && _paintedOnce)
                {
                    break;
                }
            }

            if (screenshotPath is not null && _paintedOnce)
            {
                int rc = CaptureWindowToPng(screenshotPath);
                _exitCode = rc;
            }

            DestroyWindow(_hwnd);
            _hwnd = 0;
            FreeResources();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: GUI failure: {ex.Message}");
            return ExitUnexpected;
        }

        return _exitCode;
    }

    private static void RegisterWindowClass()
    {
        var wc = new WndClassEx();
        wc.cbSize = (uint)Marshal.SizeOf<WndClassEx>();
        wc.style = CsHRedraw | CsVRedraw;
        wc.lpfnWndProc = WndProcPtr;
        wc.hInstance = _hInstance;
        wc.hCursor = LoadCursorW(0, new(32512)); // IDC_ARROW
        wc.hbrBackground = 0;
        wc.lpszClassName = Marshal.StringToHGlobalUni(MainWindowClass);

        // Load Tyler's icon from the embedded resource and attach it to the
        // window class so the taskbar / alt-tab / title bar use it by default.
        // A failure falls back gracefully (window still opens, just without a
        // custom icon) rather than aborting the GUI.
        LoadEmbeddedIcon();
        wc.hIcon = _hIconBig;
        wc.hIconSm = _hIconSmall;

        if (RegisterClassExW(ref wc) == 0)
        {
            throw new InvalidOperationException("RegisterClassExW failed");
        }

        var btn = new WndClassEx();
        btn.cbSize = (uint)Marshal.SizeOf<WndClassEx>();
        btn.style = CsHRedraw | CsVRedraw;
        btn.lpfnWndProc = ButtonWndProcPtr;
        btn.hInstance = _hInstance;
        btn.hCursor = LoadCursorW(0, new(32512));
        btn.hbrBackground = 0;
        btn.lpszClassName = Marshal.StringToHGlobalUni(ButtonClass);

        if (RegisterClassExW(ref btn) == 0)
        {
            throw new InvalidOperationException("RegisterClassExW (button) failed");
        }
    }

    private static void CreateControls(string? screenshotPath)
    {
        _hwnd = CreateWindowExW(
            WsExAppWindow,
            MainWindowClass,
            "Tyler",
            WsOverlappedWindow | WsClipChildren | WsClipSiblings,
            0, 0, DefaultClientWidth, DefaultClientHeight,
            0, 0, _hInstance, 0);

        if (_hwnd != 0)
        {
            // Explicitly push the icons to this window (in addition to the
            // class-level hIcon/hIconSm) so the taskbar and title bar always
            // show Tyler's icon even if the shell cached another class icon.
            if (_hIconBig != 0)
            {
                _ = SendMessageW(_hwnd, WmSetIcon, IconBig, _hIconBig);
            }
            if (_hIconSmall != 0)
            {
                _ = SendMessageW(_hwnd, WmSetIcon, IconSmall, _hIconSmall);
            }
        }
    }

    // ==== DPI ====
    private static void TrySetProcessDpiAwareness()
    {
        try
        {
            SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2);
        }
        catch
        {
            // Older Windows: ignore, the window still works with system DPI.
        }
    }

    // ==== LAYOUT ====
    private const int DefaultClientWidth = 560;
    private const int DefaultClientHeight = 520;

    private static void ApplyLayout()
    {
        if (_hwnd == 0) return;

        _scale = Math.Max(1.0f, GetDpiForWindow(_hwnd) / 96.0f);
        int pad = S(16);
        int pad2 = S(10);
        int labelH = S(16);
        int editH = S(30);
        int rowGap = S(8);

        // Client rect.
        _ = GetClientRect(_hwnd, out RECT client);
        int width = client.Right;
        int height = client.Bottom;

        // Title.
        int y = S(12);
        int titleH = S(28);

        // Input row.
        int row1 = y + titleH + S(14);
        SetLabel(0, row1 - labelH, "Input image");
        PositionRow(row1, CtlInputEdit, _hInputEdit, width, pad, editH);
        PositionBrowse(row1, CmdInputBrowse, _hBrowseInButton, width, pad, editH);

        // Output row.
        int row2 = row1 + editH + rowGap + labelH + S(4);
        SetLabel(0, row2 - labelH, "Output PDF");
        PositionRow(row2, CtlOutputEdit, _hOutputEdit, width, pad, editH);
        PositionBrowse(row2, CmdOutputBrowse, _hBrowseOutButton, width, pad, editH);

        // Options panel.
        int panelY = row2 + editH + rowGap + S(6);
        int panelH = S(160);

        // Option rows inside the panel.
        int oy = panelY + pad2;
        int labelW = S(120);
        int valueW = S(140);
        int vx = pad + labelW + S(8);

        SetLabel(pad, oy, "Paper size");
        MoveWindow(_hPaperCombo, vx, oy - 3, valueW, S(300), true);

        oy += S(34);
        SetLabel(pad, oy, "Orientation");
        MoveWindow(_hOrientationCombo, vx, oy - 3, valueW, S(300), true);

        oy += S(34);
        SetLabel(pad, oy, "Columns");
        MoveWindow(_hColumnsCombo, vx, oy - 3, valueW, S(300), true);

        oy += S(34);
        SetLabel(pad, oy, "Margin (mm)");
        MoveWindow(_hMarginEdit, vx, oy - 3, valueW, editH, true);

        oy += S(34);
        SetLabel(pad, oy, "Overlap (%)");
        MoveWindow(_hOverlapEdit, vx, oy - 3, valueW, editH, true);

        // Convert button (owner-drawn custom control).
        int btnW = S(160);
        int btnH = S(40);
        int btnX = pad + ((width - 2 * pad - btnW) / 2);
        int btnY = height - btnH - pad;
        MoveWindow(_hConvertButton, btnX, btnY, btnW, btnH, true);

        _panelRect = new RECT(pad, panelY, width - pad, panelY + panelH);
        _buttonRect = new RECT(btnX, btnY, btnX + btnW, btnY + btnH);
    }

    private static RECT _panelRect;
    private static RECT _buttonRect;

    private static int S(float v) => (int)MathF.Round(v * _scale);

    private static void SetLabel(int x, int y, string text)
    {
        // Static labels are drawn directly in WM_PAINT; nothing to create here.
    }

    private static void PositionRow(int y, int id, nint hEdit, int width, int pad, int editH)
    {
        int btnW = S(92);
        int gap = S(8);
        int editX = pad;
        int editW = width - 2 * pad - btnW - gap;
        MoveWindow(hEdit, editX, y, editW, editH, true);
    }

    private static void PositionBrowse(int y, int id, nint hBtn, int width, int pad, int editH)
    {
        int btnW = S(92);
        int gap = S(8);
        int editX = pad;
        int editW = width - 2 * pad - btnW - gap;
        int btnX = editX + editW + gap;
        MoveWindow(hBtn, btnX, y, btnW, editH, true);
    }

    // ==== WINDOW PROCEDURES ====
    private static bool _messageLoopRunning;
    private static bool _paintedOnce;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static unsafe nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmCreate:
            {
                _hwnd = hWnd;
                InitResources();
                CreateChildControls();
                ApplyDwmTheme();
                DragAcceptFiles(hWnd, true);
                return 0;
            }

            case WmGetMinMaxInfo:
            {
                if (lParam != 0)
                {
                    ref MinMaxInfo mmi = ref Unsafe.AsRef<MinMaxInfo>((void*)lParam);
                    mmi.ptMinTrackSize = new POINT { X = S(480), Y = S(440) };
                }
                return 0;
            }

            case WmEraseBkgnd:
                return 1; // background drawn in WM_PAINT

            case WmSize:
                if (_hwnd != 0 && wParam != 1 /* SIZE_MINIMIZED */)
                {
                    ApplyLayout();
                    InvalidateRect(_hwnd, 0, false);
                }
                return 0;

            case WmSetCursor:
            {
                (int cx, int cy) = GetCursorPos2();
                if (_buttonRect.Contains(cx, cy))
                {
                    SetCursor(LoadCursorW(0, IdcHand));
                    return 1;
                }
                return 0;
            }

            case WmGetDlgCode:
                return (nint)(0x0001 /* DLGC_WANTARROWS */ | 0x0020 /* DLGC_WANTCHARS */);

            case WmCommand:
            {
                int id = (int)(wParam & 0xFFFF);
                int code = (int)((wParam >> 16) & 0xFFFF);
                switch (id)
                {
                    case CmdInputBrowse:
                        BrowseInput();
                        return 0;
                    case CmdOutputBrowse:
                        BrowseOutput();
                        return 0;
                    case CmdConvert:
                        StartConvert();
                        return 0;
                }
                break;
            }

            case WmDropFiles:
            {
                HandleDropFiles((nint)wParam);
                return 0;
            }

            case WmCtlColorEdit:
            case WmCtlColorListbox:
            {
                SetBkColor((nint)wParam, Rgb(38, 38, 38));
                SetTextColor((nint)wParam, Rgb(220, 220, 220));
                return (nint)_editBrush;
            }

            case WmCtlColorStatic:
            {
                SetBkColor((nint)wParam, Rgb(30, 30, 30));
                SetTextColor((nint)wParam, Rgb(160, 160, 160));
                return (nint)_bgBrush;
            }

            case WmApp:
            {
                // Background Convert finished; marshal the result to the UI.
                ShowConvertResult();
                return 0;
            }

            case WmPaint:
            {
                PaintWindow(hWnd);
                _paintedOnce = true;
                return 0;
            }

            case WmClose:
                DestroyWindow(hWnd);
                return 0;

            case WmDestroy:
                _messageLoopRunning = false;
                DragAcceptFiles(hWnd, false);
                PostQuitMessage(0);
                return 0;

            case WmNcDestroy:
                FreeResources();
                return 0;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // Owner-drawn rounded "Convert" button. A plain child window of our own
    // class that paints a rounded rect and reports clicks back to the parent.
    // Hover/down state is stored per-window instance in GWLP_USERDATA (bit
    // packed) so there is no mutable static state.
    private const nint ButtonHoverBit = 0x1;
    private const nint ButtonDownBit = 0x2;

    private static void SetButtonState(nint hWnd, bool hover, bool down)
    {
        nint state = 0;
        if (hover) state |= ButtonHoverBit;
        if (down) state |= ButtonDownBit;
        _ = SetWindowLongPtrW(hWnd, GwlpUserdata, state);
    }

    private static bool GetButtonHover(nint hWnd) => (GetWindowLongPtrW(hWnd, GwlpUserdata) & ButtonHoverBit) != 0;
    private static bool GetButtonDown(nint hWnd) => (GetWindowLongPtrW(hWnd, GwlpUserdata) & ButtonDownBit) != 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint ButtonWndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmEraseBkgnd:
                return 1;

            case WmMouseMove:
            {
                if (!GetButtonHover(hWnd))
                {
                    SetButtonState(hWnd, true, GetButtonDown(hWnd));
                    TrackMouseEvent(new TrackMouseEventStruct { cbSize = (uint)Marshal.SizeOf<TrackMouseEventStruct>(), dwFlags = TmeLeave, hwndTrack = hWnd });
                    InvalidateRect(hWnd, 0, false);
                }
                return 0;
            }

            case WmMouseLeave:
            {
                SetButtonState(hWnd, false, false);
                InvalidateRect(hWnd, 0, false);
                return 0;
            }

            case WmLButtonDown:
            {
                SetButtonState(hWnd, GetButtonHover(hWnd), true);
                InvalidateRect(hWnd, 0, false);
                return 0;
            }

            case WmLButtonUp:
            {
                bool wasDown = GetButtonDown(hWnd);
                SetButtonState(hWnd, GetButtonHover(hWnd), false);
                InvalidateRect(hWnd, 0, false);
                if (wasDown && hWnd == _hConvertButton)
                {
                    SendMessageW(GetParent(hWnd), WmCommand, (nuint)CmdConvert, 0);
                }
                return 0;
            }

            case WmPaint:
            {
                PaintButton(hWnd);
                return 0;
            }

            case WmGetDlgCode:
                return (nint)(0x0001 | 0x0020);
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void PaintWindow(nint hWnd)
    {
        _ = BeginPaint(hWnd, out PaintStruct ps);
        nint dc = ps.hdc;

        try
        {
            // Background.
            FillRect(dc, ref ps.rcPaint, _bgBrush);

            RECT client;
            _ = GetClientRect(hWnd, out client);

            // Panel.
            if (_panelRect.Right > _panelRect.Left)
            {
                RECT pr = _panelRect;
                FillRoundRect(dc, ref pr, _panelBrush, S(8));
            }

            // Title.
            DrawTitle(dc, ref client);

            // Labels.
            DrawLabels(dc);
        }
        finally
        {
            _ = EndPaint(hWnd, ref ps);
        }
    }

    private static void PaintButton(nint hWnd)
    {
        _ = BeginPaint(hWnd, out PaintStruct ps);
        nint dc = ps.hdc;

        try
        {
            RECT rc;
            _ = GetClientRect(hWnd, out rc);
            FillRoundRect(dc, ref rc, _accentBrush, S(8));

            uint textColor = Rgb(255, 255, 255);
            nint fill = _accentBrush;
            if (GetButtonDown(hWnd))
            {
                fill = _accentPressedBrush;
            }
            else if (GetButtonHover(hWnd))
            {
                fill = _accentHoverBrush;
            }

            FillRoundRect(dc, ref rc, fill, S(8));

            var label = "Convert";
            SetBkMode(dc, 1 /* TRANSPARENT */);
            SelectObject(dc, _uiFont);
            SetTextColor(dc, textColor);
            RECT tr = rc;
            DrawTextW(dc, label, -1, ref tr, DtSingleLine | DtCenter | DtVcenter | DtNoPrefix);
        }
        finally
        {
            _ = EndPaint(hWnd, ref ps);
        }
    }

    private static void FillRoundRect(nint dc, ref RECT rc, nint brush, int radius)
    {
        // Fast path: use GDI RoundRect with a null brush + fill via brush? Simpler: create a rounded region.
        nint rgn = CreateRoundRectRgn(rc.Left, rc.Top, rc.Right + 1, rc.Bottom + 1, radius, radius);
        if (rgn != 0)
        {
            FillRgn(dc, rgn, brush);
            DeleteObject(rgn);
        }
        else
        {
            FillRect(dc, ref rc, brush);
        }
    }

    private static void DrawTitle(nint dc, ref RECT client)
    {
        SetBkMode(dc, 1);
        SelectObject(dc, _titleFont);
        SetTextColor(dc, Rgb(235, 235, 235));
        RECT tr = client;
        tr.Left = S(16);
        tr.Top = S(12);
        tr.Right = client.Right - S(16);
        tr.Bottom = tr.Top + S(32);
        DrawTextW(dc, "Tyler", -1, ref tr, DtLeft | DtTop | DtNoPrefix);
    }

    private static void DrawLabels(nint dc)
    {
        // The labels are laid out in ApplyLayout; here we simply re-derive the
        // same positions so the text is painted in WM_PAINT.
        int pad = S(16);
        int labelH = S(16);
        int editH = S(30);
        int rowGap = S(8);
        int y = S(12);
        int titleH = S(28);
        int row1 = y + titleH + S(14);
        int row2 = row1 + editH + rowGap + labelH + S(4);

        SelectObject(dc, _uiFont);
        SetBkMode(dc, 1);
        SetTextColor(dc, Rgb(160, 160, 160));

        DrawLabelText(dc, pad, row1 - labelH, "Input image");
        DrawLabelText(dc, pad, row2 - labelH, "Output PDF");

        int panelY = row2 + editH + rowGap + S(6);
        int oy = panelY + S(10);
        int labelW = S(120);
        int vx = pad + labelW + S(8);
        DrawLabelText(dc, pad, oy, "Paper size");
        DrawLabelText(dc, pad, oy + S(34), "Orientation");
        DrawLabelText(dc, pad, oy + S(68), "Columns");
        DrawLabelText(dc, pad, oy + S(102), "Margin (mm)");
        DrawLabelText(dc, pad, oy + S(136), "Overlap (%)");
    }

    private static void DrawLabelText(nint dc, int x, int y, string text)
    {
        RECT rc = new(x, y, x + S(300), y + S(20));
        DrawTextW(dc, text, -1, ref rc, DtLeft | DtTop | DtNoPrefix);
    }

    // ==== RESOURCES ====
    private static void InitResources()
    {
        _bgBrush = CreateSolidBrush(Rgb(30, 30, 30));
        _panelBrush = CreateSolidBrush(Rgb(36, 36, 36));
        _accentBrush = CreateSolidBrush(Rgb(0, 120, 212));
        _accentHoverBrush = CreateSolidBrush(Rgb(0, 140, 242));
        _accentPressedBrush = CreateSolidBrush(Rgb(0, 90, 170));
        _editBrush = CreateSolidBrush(Rgb(38, 38, 38));

        _uiFont = CreateUiFont(9.0f);
        _titleFont = CreateUiFont(16.0f, bold: true);
    }

    private static nint CreateUiFont(float pointSize, bool bold = false)
    {
        nint font = CreateFontW(
            -S((int)Math.Round(pointSize * 1.3333)),
            0, 0, 0,
            bold ? 700 : 400,
            0, 0, 0,
            1 /* DEFAULT_CHARSET */,
            0, 0, 0, 0,
            "Segoe UI");

        if (font == 0)
        {
            font = (nint)GetStockObject(DefaultGuiFont);
        }

        return font;
    }

    private static void FreeResources()
    {
        SafeDeleteObject(_bgBrush);
        SafeDeleteObject(_panelBrush);
        SafeDeleteObject(_accentBrush);
        SafeDeleteObject(_accentHoverBrush);
        SafeDeleteObject(_accentPressedBrush);
        SafeDeleteObject(_editBrush);
        SafeDeleteObject(_uiFont);
        SafeDeleteObject(_titleFont);

        // Destroy the window icons exactly once each (refs are nulled).
        SafeDestroyIcon(ref _hIconBig);
        SafeDestroyIcon(ref _hIconSmall);
    }

    private static void SafeDeleteObject(nint h)
    {
        if (h != 0)
        {
            _ = DeleteObject(h);
        }
    }

    // Loads Tyler's icon (embedded resource "Tyler.ico") into two HICONs, one
    // for the small (16x16) taskbar/title-bar icon and one for the large
    // (32x32) icon. LoadImageW reads from a file path, so we materialize the
    // embedded bytes to a unique temp file, load, then delete the temp file in
    // finally (the HICON retains the decoded bits). AOT/trimming safe: the
    // embedded resource stream is never trimmed and no reflection is used.
    private static void LoadEmbeddedIcon()
    {
        try
        {
            byte[] bytes;
            using (var s = typeof(TylerGui).Assembly.GetManifestResourceStream("Tyler.ico"))
            {
                if (s == null)
                {
                    return;
                }
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                bytes = ms.ToArray();
            }

            string tempPath = Path.Combine(
                Path.GetTempPath(),
                "Tyler_wicon_" + Environment.ProcessId + "_" + Guid.NewGuid().ToString("N") + ".ico");

            try
            {
                File.WriteAllBytes(tempPath, bytes);

                _hIconSmall = LoadImageW(IntPtr.Zero, tempPath, ImageIcon, 16, 16, LrLoadFromFile);
                _hIconBig = LoadImageW(IntPtr.Zero, tempPath, ImageIcon, 32, 32, LrLoadFromFile);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Best effort cleanup; ignore if the file is briefly locked.
                }
            }
        }
        catch
        {
            // Graceful fallback: leave icons null so the window still opens
            // with the default icon rather than crashing the GUI.
            _hIconSmall = 0;
            _hIconBig = 0;
        }
    }

    private static void SafeDestroyIcon(ref nint h)
    {
        if (h != 0)
        {
            _ = DestroyIcon(h);
            h = 0;
        }
    }

    // ==== DWM THEME ====
    private static void ApplyDwmTheme()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Immersive dark mode (value 19 is used on 20H1+; 20 is the pre-20H1
        // spelling). DwmSetWindowAttribute returns an HRESULT where 0 == S_OK.
        // Try the 20H1+ attribute first, then fall back to the pre-20H1 value.
        int hr = DwmSetWindowAttribute(_hwnd, DwmwaUseImmersiveDarkMode, ref DARK_ON, sizeof(uint));
        if (hr != 0)
        {
            _ = DwmSetWindowAttribute(_hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref DARK_ON, sizeof(uint));
        }

        // Rounded corners (Windows 11). Failure is ignored on older systems.
        _ = DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref ROUND, sizeof(uint));

        // Dark caption tint. DWMWA_CAPTION_COLOR; ignored if unsupported.
        _ = DwmSetWindowAttribute(_hwnd, DwmwaCaptionColor, ref CAPTION, sizeof(uint));
    }

    private static uint DARK_ON = 1;
    private static uint ROUND = (uint)DwmcpRound;
    private static uint CAPTION = 0x001E1E1E;

    // ==== CHILD CONTROLS ====
    private static void CreateChildControls()
    {
        nint parent = _hwnd;

        _hInputEdit = CreateControl(parent, "EDIT", CtlInputEdit, EsAutohscroll | WsBorder | WsTabstop);
        _hOutputEdit = CreateControl(parent, "EDIT", CtlOutputEdit, EsAutohscroll | WsBorder | WsTabstop);

        _hPaperCombo = CreateControl(parent, "COMBOBOX", CtlPaperCombo, CbsDropdownList | WsTabstop | WsVscroll | WsBorder);
        _hOrientationCombo = CreateControl(parent, "COMBOBOX", CtlOrientationCombo, CbsDropdownList | WsTabstop | WsVscroll | WsBorder);
        _hColumnsCombo = CreateControl(parent, "COMBOBOX", CtlColumnsCombo, CbsDropdownList | WsTabstop | WsVscroll | WsBorder);
        _hMarginEdit = CreateControl(parent, "EDIT", CtlMarginEdit, EsAutohscroll | WsBorder | WsTabstop);
        _hOverlapEdit = CreateControl(parent, "EDIT", CtlOverlapEdit, EsAutohscroll | WsBorder | WsTabstop);

        _hConvertButton = CreateWindowExW(
            0, ButtonClass, "Convert",
            WsChild | WsVisible | WsTabstop,
            0, 0, 100, 40, parent, (nuint)CmdConvert, _hInstance, 0);
        _hBrowseInButton = CreateControl(parent, "BUTTON", CmdInputBrowse, WsTabstop | 0x00000000);
        _hBrowseOutButton = CreateControl(parent, "BUTTON", CmdOutputBrowse, WsTabstop);

        // Populate combo boxes.
        AddComboItems(_hPaperCombo, "A4", "Letter");
        AddComboItems(_hOrientationCombo, "Portrait", "Landscape");
        for (int i = 1; i <= 12; i++)
        {
            nint ptr = Marshal.StringToHGlobalUni(i.ToString(CultureInfo.InvariantCulture));
            _ = SendMessageW(_hColumnsCombo, CbAddString, 0, ptr);
            Marshal.FreeHGlobal(ptr);
        }

        SetComboSel(_hPaperCombo, 0);
        SetComboSel(_hOrientationCombo, 0);
        SetComboSel(_hColumnsCombo, 1); // index 1 == "2"

        // Default values for the numeric edits.
        SetEditText(_hMarginEdit, "10.0");
        SetEditText(_hOverlapEdit, "5.0");

        // Input edit is read-only (browse/drag&drop populate it).
        _ = SendMessageW(_hInputEdit, EmSetReadOnly, 1, 0);

        // Make the two edit rows' text the system UI font.
        SetEditFont(_hInputEdit);
        SetEditFont(_hOutputEdit);
        SetEditFont(_hMarginEdit);
        SetEditFont(_hOverlapEdit);
    }

    private static nint CreateControl(nint parent, string className, int id, uint style)
    {
        return CreateWindowExW(
            0, className, "", style | WsChild | WsVisible,
            0, 0, 50, 20, parent, (nuint)id, _hInstance, 0);
    }

    private static void AddComboItems(nint hCombo, params string[] items)
    {
        foreach (string item in items)
        {
            nint ptr = Marshal.StringToHGlobalUni(item);
            _ = SendMessageW(hCombo, CbAddString, 0, ptr);
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void SetComboSel(nint hCombo, int index)
    {
        _ = SendMessageW(hCombo, CbSetCurSel, (nuint)index, 0);
    }

    private static void SetEditText(nint hEdit, string text)
    {
        nint ptr = Marshal.StringToHGlobalUni(text);
        _ = SendMessageW(hEdit, 0x000C /* WM_SETTEXT */, 0, ptr);
        Marshal.FreeHGlobal(ptr);
    }

    private static void SetEditFont(nint hEdit)
    {
        _ = SendMessageW(hEdit, 0x0030 /* WM_SETFONT */, (nuint)_uiFont, 1);
    }

    // ==== CONVERT PIPELINE ====
    private static void StartConvert()
    {
        string input = GetEditText(_hInputEdit);
        if (string.IsNullOrWhiteSpace(input))
        {
            _ = MessageBoxW(_hwnd, "Choose an input image first (browse or drag & drop).", "Tyler", MbOk | MbIconInformation);
            return;
        }

        string output = GetEditText(_hOutputEdit);
        if (string.IsNullOrWhiteSpace(output))
        {
            output = Path.ChangeExtension(input, ".pdf");
            SetEditText(_hOutputEdit, output);
        }

        string paper = GetComboText(_hPaperCombo);
        string orientation = GetComboText(_hOrientationCombo);
        string columns = GetComboText(_hColumnsCombo);
        string margin = GetEditText(_hMarginEdit);
        string overlap = GetEditText(_hOverlapEdit);

        string[] args =
        {
            "--input", input,
            "--output", output,
            "--paper-size", paper,
            "--orientation", orientation,
            "--columns", columns,
            "--margin", margin,
            "--overlap", overlap,
        };

        // Disable the button while converting.
        EnableWindow(_hConvertButton, false);

        ThreadPool.QueueUserWorkItem(
            _ =>
            {
                int exitCode = TylerEngine.Run(args, out string? msg);
                _bgExitCode = exitCode;
                _bgMessage = msg;
                _ = PostMessageW(_hwnd, WmApp, 0, 0);
            });
    }

    private static void ShowConvertResult()
    {
        EnableWindow(_hConvertButton, true);

        string title = _bgExitCode == TylerEngine.ExitSuccess ? "Tyler â€” conversion complete" : "Tyler â€” conversion failed";
        uint icon = _bgExitCode == TylerEngine.ExitSuccess ? MbIconInformation : MbIconError;
        string message = _bgExitCode == TylerEngine.ExitSuccess
            ? $"PDF created:\n{_bgMessage}"
            : $"The conversion failed:\n{_bgMessage}\n\nExit code {_bgExitCode}.";

        _ = MessageBoxW(_hwnd, message, title, MbOk | icon);
    }

    private static string GetEditText(nint hEdit)
    {
        int len = (int)SendMessageW(hEdit, 0x000E /* WM_GETTEXTLENGTH */, 0, 0);
        if (len <= 0)
        {
            return string.Empty;
        }

        nint buf = Marshal.AllocHGlobal((len + 1) * 2);
        try
        {
            _ = SendMessageW(hEdit, 0x000D /* WM_GETTEXT */, (nuint)(len + 1), buf);
            return Marshal.PtrToStringUni(buf) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static string GetComboText(nint hCombo)
    {
        int sel = (int)SendMessageW(hCombo, CbGetCurSel, 0, 0);
        if (sel < 0)
        {
            return string.Empty;
        }

        int len = (int)SendMessageW(hCombo, CbGetLbTextLen, (nuint)sel, 0);
        nint buf = Marshal.AllocHGlobal((len + 1) * 2);
        try
        {
            _ = SendMessageW(hCombo, CbGetLbText, (nuint)sel, buf);
            return Marshal.PtrToStringUni(buf) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    // ==== FILE DIALOGS ====
    private static void BrowseInput()
    {
        string? path = OpenFileDialog();
        if (path is not null)
        {
            SetEditText(_hInputEdit, path);
            AutoFillOutput(path);
        }
    }

    private static void BrowseOutput()
    {
        string? path = SaveFileDialog();
        if (path is not null)
        {
            SetEditText(_hOutputEdit, path);
        }
    }

    private static string? OpenFileDialog()
    {
        var ofn = new OpenFileName();
        ofn.lStructSize = (uint)Marshal.SizeOf<OpenFileName>();
        ofn.hwndOwner = _hwnd;
        ofn.nMaxFile = 1024;
        ofn.lpstrFile = Marshal.AllocHGlobal(1024 * 2);
        ofn.Flags = OfnFileMustExist | OfnPathMustExist | OfnExplorer;

        try
        {
            ofn.lpstrFilter = Marshal.StringToHGlobalUni("Screenshots (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*||");
            ofn.lpstrTitle = Marshal.StringToHGlobalUni("Select a screenshot");

            bool ok = GetOpenFileNameW(ref ofn);
            if (!ok)
            {
                return null;
            }

            return Marshal.PtrToStringUni(ofn.lpstrFile);
        }
        finally
        {
            Marshal.FreeHGlobal(ofn.lpstrFile);
            Marshal.FreeHGlobal(ofn.lpstrFilter);
            Marshal.FreeHGlobal(ofn.lpstrTitle);
        }
    }

    private static string? SaveFileDialog()
    {
        var ofn = new OpenFileName();
        ofn.lStructSize = (uint)Marshal.SizeOf<OpenFileName>();
        ofn.hwndOwner = _hwnd;
        ofn.nMaxFile = 1024;
        ofn.lpstrFile = Marshal.AllocHGlobal(1024 * 2);
        ofn.Flags = OfnOverwriteprompt | OfnExplorer;

        try
        {
            ofn.lpstrFilter = Marshal.StringToHGlobalUni("PDF files (*.pdf)|*.pdf|All files (*.*)|*.*||");
            ofn.lpstrDefExt = Marshal.StringToHGlobalUni("pdf");
            ofn.lpstrTitle = Marshal.StringToHGlobalUni("Save PDF as");

            bool ok = GetSaveFileNameW(ref ofn);
            if (!ok)
            {
                return null;
            }

            return Marshal.PtrToStringUni(ofn.lpstrFile);
        }
        finally
        {
            Marshal.FreeHGlobal(ofn.lpstrFile);
            Marshal.FreeHGlobal(ofn.lpstrFilter);
            Marshal.FreeHGlobal(ofn.lpstrDefExt);
            Marshal.FreeHGlobal(ofn.lpstrTitle);
        }
    }

    // ==== DRAG & DROP ====
    private static void HandleDropFiles(nint hDrop)
    {
        try
        {
            uint count = DragQueryFileW(hDrop, 0xFFFFFFFF /* file index */, 0, 0);
            if (count == 0)
            {
                return;
            }

            uint len = DragQueryFileW(hDrop, 0, 0, 0);
            nint buf = Marshal.AllocHGlobal((int)(len + 1) * 2);
            try
            {
                _ = DragQueryFileW(hDrop, 0, buf, len + 1);
                string? path = Marshal.PtrToStringUni(buf);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    SetEditText(_hInputEdit, path);
                    AutoFillOutput(path);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        finally
        {
            DragFinish(hDrop);
        }
    }

    // Auto-fill the output field with the .pdf path derived from the input,
    // but only when the output field is currently empty (never overwrite user
    // content).
    private static void AutoFillOutput(string inputPath)
    {
        if (!string.IsNullOrWhiteSpace(GetEditText(_hOutputEdit)))
        {
            return;
        }

        string output = Path.ChangeExtension(inputPath, ".pdf");
        SetEditText(_hOutputEdit, output);
    }

    // ==== SELF-SCREENSHOT CAPTURE (--gui-screenshot) ====
    private static int CaptureWindowToPng(string outputPath)
    {
        try
        {
            _ = GetWindowRect(_hwnd, out RECT wr);
            int width = wr.Right - wr.Left;
            int height = wr.Bottom - wr.Top;

            if (width <= 0 || height <= 0)
            {
                return ExitUnexpected;
            }

            nint hdcScreen = GetWindowDC(_hwnd);
            nint memDc = CreateCompatibleDC(hdcScreen);

            var bi = new BitmapInfo();
            bi.bmiHeader.biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>();
            bi.bmiHeader.biWidth = width;
            bi.bmiHeader.biHeight = -height; // top-down
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            bi.bmiHeader.biCompression = BiRgb;

            nint section = CreateDIBSection(hdcScreen, ref bi, DibRgbColors, out nint bits, 0, 0);
            nint old = SelectObject(memDc, section);

            try
            {
                // PrintWindow with PW_RENDERFULLCONTENT renders the DWM
                // composition (rounded corners + dark caption). Fall back to
                // PW_CLIENTONLY if it fails (older Windows 10 without DWM).
                bool ok = PrintWindow(_hwnd, memDc, PwRenderFullContent);
                if (!ok)
                {
                    ok = PrintWindow(_hwnd, memDc, PwClientOnly);
                }

                if (!ok)
                {
                    _ = BitBlt(memDc, 0, 0, width, height, hdcScreen, 0, 0, SrcCopy);
                }

                unsafe
                {
                    using var image = Image.LoadPixelData<Bgra32>(new Span<Bgra32>((void*)bits, width * height), width, height);
                    image.Save(outputPath, new PngEncoder());
                }
            }
            finally
            {
                _ = SelectObject(memDc, old);
                _ = DeleteObject(section);
                _ = DeleteDC(memDc);
                _ = ReleaseDC(_hwnd, hdcScreen);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: capture failed: {ex.Message}");
            return ExitUnexpected;
        }

        return ExitSuccess;
    }

    // ==== RECT / POINT HELPERS ====
    private static bool Contains(this RECT r, int x, int y) =>
        x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    private static (int X, int Y) GetCursorPos2()
    {
        _ = GetCursorPos(out POINT p);
        return (p.X, p.Y);
    }

    // ==== P/INVOKE (source-generated [LibraryImport] - AOT safe) ====
    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics", SetLastError = true)]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint dpiAwareness);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow", SetLastError = true)]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial nint GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WndClassEx lpWndClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static partial nint CreateWindowExW(
        uint dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nuint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProcW(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    private static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursorW(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll", EntryPoint = "SetCursor")]
    private static partial nint SetCursor(nint hCursor);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "MoveWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveWindow(nint hWnd, int x, int y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    [LibraryImport("user32.dll", EntryPoint = "InvalidateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("user32.dll", EntryPoint = "GetParent")]
    private static partial nint GetParent(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "EnableWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
    private static partial nint SendMessageW(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true)]
    private static partial nint LoadImageW(nint hInst, [MarshalAs(UnmanagedType.LPWStr)] string name, uint type, int cx, int cy, uint fuLoad);

    [LibraryImport("user32.dll", EntryPoint = "DestroyIcon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowDC")]
    private static partial nint GetWindowDC(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
    private static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("user32.dll", EntryPoint = "BeginPaint")]
    private static partial nint BeginPaint(nint hWnd, out PaintStruct lpPaint);

    [LibraryImport("user32.dll", EntryPoint = "EndPaint")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndPaint(nint hWnd, ref PaintStruct lpPaint);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW")]
    private static partial int MessageBoxW(nint hWnd, [MarshalAs(UnmanagedType.LPWStr)] string lpText, [MarshalAs(UnmanagedType.LPWStr)] string lpCaption, uint uType);

    [LibraryImport("shell32.dll", EntryPoint = "DragAcceptFiles")]
    private static partial void DragAcceptFiles(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool fAccept);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW")]
    private static partial uint DragQueryFileW(nint hDrop, uint iFile, nint lpszFile, uint cch);

    [LibraryImport("shell32.dll", EntryPoint = "DragFinish")]
    private static partial void DragFinish(nint hDrop);

    [LibraryImport("user32.dll", EntryPoint = "TrackMouseEvent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TrackMouseEvent(TrackMouseEventStruct lpEventTrack);

    [LibraryImport("gdi32.dll", EntryPoint = "GetStockObject")]
    private static partial nint GetStockObject(int i);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint hObject);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateSolidBrush")]
    private static partial nint CreateSolidBrush(uint crColor);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW")]
    private static partial nint CreateFontW(
        int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
        uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet,
        uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily,
        [MarshalAs(UnmanagedType.LPWStr)] string pszFaceName);

    [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
    private static partial nint SelectObject(nint hdc, nint hObject);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("user32.dll", EntryPoint = "FillRect")]
    private static partial int FillRect(nint hdc, ref RECT lprc, nint hbr);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkColor")]
    private static partial uint SetBkColor(nint hdc, uint crColor);

    [LibraryImport("gdi32.dll", EntryPoint = "SetTextColor")]
    private static partial uint SetTextColor(nint hdc, uint crColor);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkMode")]
    private static partial int SetBkMode(nint hdc, int iBkMode);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW")]
    private static partial int DrawTextW(nint hdc, [MarshalAs(UnmanagedType.LPWStr)] string lpchText, int cchText, ref RECT lprc, uint format);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [LibraryImport("gdi32.dll", EntryPoint = "FillRgn")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FillRgn(nint hdc, nint hrgn, nint hbr);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
    private static partial nint CreateDIBSection(nint hdc, ref BitmapInfo pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

    [LibraryImport("gdi32.dll", EntryPoint = "BitBlt")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint hdcDest, int x, int y, int w, int h, nint hdcSrc, int x1, int y1, uint rop);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute", SetLastError = true)]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

    [LibraryImport("user32.dll", EntryPoint = "PrintWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileNameW(ref OpenFileName lpofn);

    [LibraryImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSaveFileNameW(ref OpenFileName lpofn);

    // ==== WINDOW PROCEDURE FUNCTION POINTERS ====
    // [UnmanagedCallersOnly] methods cannot be converted to a managed delegate;
    // use raw unmanaged function pointers (AOT safe, no GC root required).
    private static readonly unsafe nint WndProcPtr = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WndProc;
    private static readonly unsafe nint ButtonWndProcPtr = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&ButtonWndProc;

    // ==== WIN32 STRUCTS ====
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public RECT(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public int rgbReserved0;
        public int rgbReserved1;
        public int rgbReserved2;
        public int rgbReserved3;
        public int rgbReserved4;
        public int rgbReserved5;
        public int rgbReserved6;
        public int rgbReserved7;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventStruct
    {
        public uint cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public nint lpstrFilter;
        public nint lpstrCustomFilter;
        public uint nMaxCustFilter;
        public uint nFilterIndex;
        public nint lpstrFile;
        public uint nMaxFile;
        public nint lpstrFileTitle;
        public uint nMaxFileTitle;
        public nint lpstrInitialDir;
        public nint lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public nint lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
    }
}
