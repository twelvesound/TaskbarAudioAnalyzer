using System.Diagnostics;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace TaskbarAudioAnalyzer;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const double DefaultWidth = 652;
    private const double FallbackTaskbarHeight = 48;
    private const double LoudnessDeadbandDb = 0.2;
    private static readonly TimeSpan LoudnessRenderInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PhaseRenderInterval = TimeSpan.FromMilliseconds(100);
    private const int SpectrumBarCount = AudioAnalyzer.SpectrumBarCount;
    private static readonly SolidColorBrush[] MeterHeatBrushes = CreateMeterHeatBrushes();

    private readonly DispatcherTimer renderTimer = new();
    private readonly AudioAnalyzer analyzer = new();
    private readonly List<Rectangle> spectrumBars = [];
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "12sound",
        "TaskbarAudioAnalyzer",
        "settings.json");
    private System.Windows.Forms.NotifyIcon? trayIcon;
    private System.Drawing.Icon? trayIconImage;
    private DateTimeOffset lastStartAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset lastLoudnessRenderAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastPhaseRenderAt = DateTimeOffset.MinValue;
    private double? displayedLoudnessDb;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshStartupCommandIfEnabled();

        if (!LoadPlacement())
        {
            PlaceNearTaskbar();
        }

        CreateSpectrumBars();
        CreateTrayIcon();
        TryStartAnalyzer();

        renderTimer.Interval = TimeSpan.FromMilliseconds(50);
        renderTimer.Tick += RenderTimer_Tick;
        renderTimer.Start();

        Activate();
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(3000);
            Topmost = false;
        });
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        if (!analyzer.IsCapturing && DateTimeOffset.UtcNow - lastStartAttempt >= TimeSpan.FromSeconds(2))
        {
            TryStartAnalyzer();
        }

        var snapshot = analyzer.GetSnapshot();
        var now = DateTimeOffset.UtcNow;
        if (now - lastLoudnessRenderAt >= LoudnessRenderInterval)
        {
            lastLoudnessRenderAt = now;
            UpdateLoudnessDisplay(snapshot.LoudnessDb);
        }

        TruePeakText.Text = FormatDecibels(snapshot.TruePeakDb);
        TruePeakText.Foreground = GetMeterHeatBrush(snapshot.TruePeakDb, -8, 0);
        InputStatusText.Text = FormatInputStatus(snapshot);

        if (now - lastPhaseRenderAt >= PhaseRenderInterval)
        {
            lastPhaseRenderAt = now;
            UpdatePhaseDisplay(snapshot.PhaseCorrelation);
        }

        DrawSpectrum(snapshot.Spectrum);
    }

    private void UpdateLoudnessDisplay(double loudnessDb)
    {
        if (displayedLoudnessDb is double displayed &&
            Math.Abs(loudnessDb - displayed) < LoudnessDeadbandDb)
        {
            return;
        }

        displayedLoudnessDb = loudnessDb;
        LoudnessText.Text = FormatDecibels(loudnessDb);
        LoudnessText.Foreground = GetMeterHeatBrush(loudnessDb, -14, -8);
    }

    private void UpdatePhaseDisplay(double? phaseCorrelation)
    {
        if (phaseCorrelation is not double value || !double.IsFinite(value))
        {
            PhaseValueText.Text = "—";
            PhaseMarker.Visibility = Visibility.Collapsed;
            return;
        }

        value = Math.Clamp(value, -1, 1);
        PhaseValueText.Text = value.ToString("+0.00;-0.00;0.00");
        PhaseMarker.Visibility = Visibility.Visible;

        var trackWidth = PhaseMeterTrack.ActualWidth;
        if (trackWidth <= PhaseMarker.Width)
        {
            return;
        }

        var normalized = (value + 1) / 2;
        var markerLeft = normalized * trackWidth - PhaseMarker.Width / 2;
        Canvas.SetLeft(PhaseMarker, Math.Clamp(markerLeft, 0, trackWidth - PhaseMarker.Width));
    }

    private void TryStartAnalyzer()
    {
        lastStartAttempt = DateTimeOffset.UtcNow;
        analyzer.TryStart();
    }

    private void CreateSpectrumBars()
    {
        SpectrumCanvas.Children.Clear();
        spectrumBars.Clear();

        var brush = new SolidColorBrush(Color.FromRgb(110, 231, 245));
        brush.Freeze();

        for (var i = 0; i < SpectrumBarCount; i++)
        {
            var bar = new Rectangle
            {
                Fill = brush,
                RadiusX = 1,
                RadiusY = 1,
                Opacity = 0.82,
                Height = 1
            };
            spectrumBars.Add(bar);
            SpectrumCanvas.Children.Add(bar);
        }
    }

    private void DrawSpectrum(IReadOnlyList<float> spectrum)
    {
        var width = SpectrumCanvas.ActualWidth;
        var height = SpectrumCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        const double gap = 2;
        var barWidth = Math.Max(1, (width - gap * (SpectrumBarCount - 1)) / SpectrumBarCount);

        for (var i = 0; i < spectrumBars.Count; i++)
        {
            var normalized = i < spectrum.Count ? Math.Clamp(spectrum[i], 0, 1) : 0;
            var barHeight = Math.Max(1, normalized * height);
            var bar = spectrumBars[i];
            bar.Width = barWidth;
            bar.Height = barHeight;
            Canvas.SetLeft(bar, i * (barWidth + gap));
            Canvas.SetTop(bar, height - barHeight);
        }
    }

    private void PlaceNearTaskbar()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var workArea = SystemParameters.WorkArea;

        Width = Math.Min(DefaultWidth, screenWidth - 12);
        Height = GetTaskbarHeight();
        Left = (screenWidth - Width) / 2;
        Top = workArea.Bottom < screenHeight ? screenHeight - Height : screenHeight - Height - 12;

        Left = Math.Max(0, Math.Min(Left, screenWidth - Width));
        Top = Math.Max(0, Math.Min(Top, screenHeight - Height));
    }

    private bool LoadPlacement()
    {
        try
        {
            var loadPath = TryMigrateLegacySettings() ?? settingsPath;
            if (!File.Exists(loadPath))
            {
                return false;
            }

            var settings = JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(loadPath));
            if (settings is null)
            {
                return false;
            }

            Width = Math.Min(DefaultWidth, SystemParameters.PrimaryScreenWidth - 12);
            Height = GetTaskbarHeight();
            Left = Math.Clamp(settings.Left, 0, Math.Max(0, SystemParameters.PrimaryScreenWidth - Width));
            Top = Math.Clamp(settings.Top, 0, Math.Max(0, SystemParameters.PrimaryScreenHeight - Height));
            analyzer.ConfigureSource(settings.AudioSourceKind, settings.AudioDeviceId, settings.AudioDeviceName);
            analyzer.ConfigureMixMode(settings.InputMixMode);
            analyzer.ConfigureInputTrims(settings.WindowsTrimDb, settings.VstTrimDb);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SavePlacement()
    {
        string? temporaryPath = null;
        try
        {
            var source = analyzer.SelectedSource;
            var settings = new WindowSettings(
                Left,
                Top,
                Width,
                Height,
                source.Kind,
                source.DeviceId,
                source.DisplayName,
                analyzer.MixMode,
                analyzer.WindowsTrimDb,
                analyzer.VstTrimDb);
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            temporaryPath = $"{settingsPath}.{Environment.ProcessId}.tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, settingsPath, true);
        }
        catch
        {
            // The analyzer should still exit cleanly if its directory becomes read-only.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // A failed cleanup must not interfere with shutdown.
                }
            }
        }
    }

    private string? TryMigrateLegacySettings()
    {
        if (File.Exists(settingsPath))
        {
            return settingsPath;
        }

        foreach (var legacyPath in GetLegacySettingsPaths()
                     .Where(File.Exists)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                if (JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(legacyPath)) is null)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                File.Copy(legacyPath, settingsPath, false);
                return settingsPath;
            }
            catch
            {
                // If migration is blocked, load the legacy file without modifying it.
                return legacyPath;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetLegacySettingsPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "settings.json");

        var projectRoot = FindProjectRoot();
        if (projectRoot is null)
        {
            yield break;
        }

        yield return Path.Combine(
            projectRoot,
            "bin",
            "Debug",
            "net10.0-windows",
            "settings.json");
        yield return Path.Combine(
            projectRoot,
            "artifacts",
            "legacy-build",
            "analyzer",
            "bin",
            "Debug",
            "net10.0-windows",
            "settings.json");
    }

    private static string? FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            var project = Path.Combine(
                directory.FullName,
                "src",
                "TaskbarAudioAnalyzer",
                "TaskbarAudioAnalyzer.csproj");
            var startScript = Path.Combine(directory.FullName, "scripts", "Start-Analyzer.ps1");
            if (File.Exists(project) && File.Exists(startScript))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static double GetTaskbarHeight()
    {
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var bottomTaskbarHeight = screenHeight - SystemParameters.WorkArea.Bottom;
        return bottomTaskbarHeight > 0
            ? Math.Clamp(bottomTaskbarHeight, 32, 96)
            : FallbackTaskbarHeight;
    }

    private void Shell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
            SavePlacement();
        }
        catch
        {
            // DragMove can throw if the mouse is released during the call.
        }
    }

    private void EnableStartupMenuItem_Click(object sender, RoutedEventArgs e)
        => EnableStartup();

    private void EnableStartup()
    {
        try
        {
            Directory.CreateDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            File.WriteAllText(GetStartupCommandPath(), GetStartupCommandText());
        }
        catch
        {
            // Keep the context-menu action unobtrusive on locked-down systems.
        }
    }

    private void DisableStartupMenuItem_Click(object sender, RoutedEventArgs e)
        => DisableStartup();

    private void DisableStartup()
    {
        try
        {
            var path = GetStartupCommandPath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Keep the context-menu action unobtrusive on locked-down systems.
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        renderTimer.Stop();
        analyzer.Dispose();
        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        trayIconImage?.Dispose();
        trayIconImage = null;
        SavePlacement();
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var sourceMenu = new System.Windows.Forms.ToolStripMenuItem("Audio source");
        sourceMenu.DropDownOpening += (_, _) => RebuildSourceMenu(sourceMenu);
        menu.Items.Add(sourceMenu);

        var inputModeMenu = new System.Windows.Forms.ToolStripMenuItem("Input mode");
        inputModeMenu.DropDownOpening += (_, _) => RebuildInputModeMenu(inputModeMenu);
        menu.Items.Add(inputModeMenu);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var enableStartup = new System.Windows.Forms.ToolStripMenuItem("Enable startup");
        enableStartup.Click += (_, _) => Dispatcher.Invoke(EnableStartup);
        menu.Items.Add(enableStartup);

        var disableStartup = new System.Windows.Forms.ToolStripMenuItem("Disable startup");
        disableStartup.Click += (_, _) => Dispatcher.Invoke(DisableStartup);
        menu.Items.Add(disableStartup);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exit = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Dispatcher.Invoke(Close);
        menu.Items.Add(exit);

        trayIconImage = CreateTrayIconImage();
        trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Taskbar Audio Analyzer",
            Icon = trayIconImage,
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private void RebuildSourceMenu(System.Windows.Forms.ToolStripMenuItem sourceMenu)
    {
        sourceMenu.DropDownItems.Clear();
        var sources = AudioAnalyzer.GetAvailableSources();
        var selected = analyzer.SelectedSource;

        AddSourceMenuItem(sourceMenu, sources[0]);
        AddSourceGroup(sourceMenu, "Playback devices (loopback)", sources.Where(source => source.Kind == AudioSourceKind.PlaybackLoopback));
        AddSourceGroup(sourceMenu, "Recording inputs", sources.Where(source => source.Kind == AudioSourceKind.RecordingInput));

        if (selected.Kind != AudioSourceKind.DefaultPlaybackLoopback &&
            !sources.Any(source => source.Kind == selected.Kind && source.DeviceId == selected.DeviceId))
        {
            sourceMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            sourceMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem($"Unavailable: {selected.DisplayName}")
            {
                Checked = true,
                Enabled = false
            });
        }
    }

    private void RebuildInputModeMenu(System.Windows.Forms.ToolStripMenuItem inputModeMenu)
    {
        inputModeMenu.DropDownItems.Clear();
        AddInputModeMenuItem(inputModeMenu, "Auto Mix (Windows + VST)", InputMixMode.AutoMix);
        AddInputModeMenuItem(inputModeMenu, "Windows only", InputMixMode.WindowsOnly);
        AddInputModeMenuItem(inputModeMenu, "VST only", InputMixMode.VstOnly);
        inputModeMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        AddTrimMenu(inputModeMenu, "Windows trim", true, analyzer.WindowsTrimDb);
        AddTrimMenu(inputModeMenu, "VST trim", false, analyzer.VstTrimDb);
    }

    private void AddInputModeMenuItem(
        System.Windows.Forms.ToolStripMenuItem parent,
        string text,
        InputMixMode mode)
    {
        var item = new System.Windows.Forms.ToolStripMenuItem(text)
        {
            Checked = analyzer.MixMode == mode
        };
        item.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            analyzer.ConfigureMixMode(mode);
            SavePlacement();
        });
        parent.DropDownItems.Add(item);
    }

    private void AddTrimMenu(
        System.Windows.Forms.ToolStripMenuItem parent,
        string text,
        bool isWindows,
        double selectedTrimDb)
    {
        var trimMenu = new System.Windows.Forms.ToolStripMenuItem(text);
        foreach (var trimDb in new[] { -12.0, -6.0, -3.0, 0.0, 3.0, 6.0 })
        {
            var label = trimDb > 0 ? $"+{trimDb:0} dB" : $"{trimDb:0} dB";
            var item = new System.Windows.Forms.ToolStripMenuItem(label)
            {
                Checked = Math.Abs(selectedTrimDb - trimDb) < 0.01
            };
            item.Click += (_, _) => Dispatcher.Invoke(() =>
            {
                analyzer.ConfigureInputTrims(
                    isWindows ? trimDb : analyzer.WindowsTrimDb,
                    isWindows ? analyzer.VstTrimDb : trimDb);
                SavePlacement();
            });
            trimMenu.DropDownItems.Add(item);
        }

        parent.DropDownItems.Add(trimMenu);
    }

    private void AddSourceGroup(
        System.Windows.Forms.ToolStripMenuItem parent,
        string heading,
        IEnumerable<AudioSourceInfo> sources)
    {
        parent.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        parent.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem(heading) { Enabled = false });

        var sourceList = sources.ToList();
        if (sourceList.Count == 0)
        {
            parent.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("(none available)") { Enabled = false });
            return;
        }

        foreach (var source in sourceList)
        {
            AddSourceMenuItem(parent, source);
        }
    }

    private void AddSourceMenuItem(System.Windows.Forms.ToolStripMenuItem parent, AudioSourceInfo source)
    {
        var selected = analyzer.SelectedSource;
        var item = new System.Windows.Forms.ToolStripMenuItem(source.DisplayName)
        {
            Checked = source.Kind == selected.Kind && source.DeviceId == selected.DeviceId
        };
        item.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            analyzer.SelectSource(source);
            lastStartAttempt = DateTimeOffset.UtcNow;
            SavePlacement();
        });
        parent.DropDownItems.Add(item);
    }

    private static System.Drawing.Icon CreateTrayIconImage()
    {
        using var bitmap = new System.Drawing.Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent);

        using var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(235, 24, 30, 36));
        using var bars = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 110, 231, 245));
        graphics.FillRoundedRectangle(
            background,
            new System.Drawing.Rectangle(0, 0, 16, 16),
            new System.Drawing.Size(3, 3));
        graphics.FillRectangle(bars, 3, 9, 2, 4);
        graphics.FillRectangle(bars, 7, 4, 2, 9);
        graphics.FillRectangle(bars, 11, 7, 2, 6);

        var handle = bitmap.GetHicon();
        try
        {
            return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static string GetStartupCommandPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "TaskbarAudioAnalyzer.cmd");

    private static void RefreshStartupCommandIfEnabled()
    {
        try
        {
            var path = GetStartupCommandPath();
            if (File.Exists(path))
            {
                File.WriteAllText(path, GetStartupCommandText());
            }
        }
        catch
        {
            // Startup repair is best-effort on locked-down systems.
        }
    }

    private static string GetStartupCommandText()
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var commandPath = exePath.Replace("%", "%%", StringComparison.Ordinal);
        return $"@echo off{Environment.NewLine}start \"\" \"{commandPath}\"{Environment.NewLine}";
    }

    private static string FormatDecibels(double value)
        => double.IsFinite(value) && value > -90 ? value.ToString("0.0") : "-∞";

    private static string FormatInputStatus(AnalyzerSnapshot snapshot)
    {
        return snapshot.MixMode switch
        {
            InputMixMode.WindowsOnly => snapshot.WindowsConnected ? "WIN" : "WIN—",
            InputMixMode.VstOnly => snapshot.VstConnected ? "VST" : "VST—",
            _ when snapshot.WindowsConnected && snapshot.VstConnected => "WIN+VST",
            _ when snapshot.VstConnected => "VST",
            _ when snapshot.WindowsConnected => "WIN",
            _ => "NO INPUT"
        };
    }

    private static SolidColorBrush GetMeterHeatBrush(double value, double whiteAt, double redAt)
    {
        if (!double.IsFinite(value))
        {
            return MeterHeatBrushes[0];
        }

        var normalized = Math.Clamp((value - whiteAt) / (redAt - whiteAt), 0, 1);
        var index = (int)Math.Round(normalized * (MeterHeatBrushes.Length - 1));
        return MeterHeatBrushes[index];
    }

    private static SolidColorBrush[] CreateMeterHeatBrushes()
    {
        const int stepCount = 65;
        var brushes = new SolidColorBrush[stepCount];
        var start = Color.FromRgb(244, 247, 250);
        var end = Color.FromRgb(255, 32, 32);

        for (var i = 0; i < stepCount; i++)
        {
            var amount = (double)i / (stepCount - 1);
            var color = Color.FromRgb(
                (byte)Math.Round(start.R + (end.R - start.R) * amount),
                (byte)Math.Round(start.G + (end.G - start.G) * amount),
                (byte)Math.Round(start.B + (end.B - start.B) * amount));
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            brushes[i] = brush;
        }

        return brushes;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}

internal sealed record WindowSettings(
    double Left,
    double Top,
    double Width,
    double Height,
    AudioSourceKind AudioSourceKind = AudioSourceKind.DefaultPlaybackLoopback,
    string? AudioDeviceId = null,
    string? AudioDeviceName = null,
    InputMixMode InputMixMode = InputMixMode.AutoMix,
    double WindowsTrimDb = 0,
    double VstTrimDb = 0);

internal sealed record AnalyzerSnapshot(
    double LoudnessDb,
    double TruePeakDb,
    double? PhaseCorrelation,
    bool WindowsConnected,
    bool VstConnected,
    InputMixMode MixMode,
    float[] Spectrum);

internal enum AudioSourceKind
{
    DefaultPlaybackLoopback,
    PlaybackLoopback,
    RecordingInput
}

internal sealed record AudioSourceInfo(AudioSourceKind Kind, string? DeviceId, string DisplayName);

internal sealed class AudioAnalyzer : IDisposable
{
    public const int SpectrumBarCount = 28;

    private const int FftLength = 2048;
    private const int FftExponent = 11;
    private const double FloorDb = -90;
    private const double PhaseSmoothingSeconds = 0.4;
    private const int MixerSampleRate = 48000;
    private const int MixerBlockFrames = 480;
    private const int InputQueueCapacityFrames = MixerSampleRate * 2;

    private readonly object lifecycleGate = new();
    private readonly object metricsGate = new();
    private readonly object analysisGate = new();
    private readonly Complex[] fftBuffer = new Complex[FftLength];
    private readonly float[] fftInput = new float[FftLength];
    private readonly float[] spectrum = new float[SpectrumBarCount];
    private readonly StereoSampleQueue windowsQueue = new(InputQueueCapacityFrames);
    private readonly StereoSampleQueue vstQueue = new(InputQueueCapacityFrames);
    private readonly StreamingStereoResampler windowsResampler = new(MixerSampleRate);
    private readonly StreamingStereoResampler vstResampler = new(MixerSampleRate);
    private readonly ShortTermLoudnessMeter shortTermLoudnessMeter = new(MixerSampleRate);
    private readonly VstTapReceiver vstReceiver = new();
    private readonly CancellationTokenSource mixerCancellation = new();
    private readonly Thread mixerThread;

    private IWaveIn? capture;
    private MMDevice? captureDevice;
    private WaveFormat? waveFormat;
    private AudioSourceInfo selectedSource = new(
        AudioSourceKind.DefaultPlaybackLoopback,
        null,
        "Default playback (loopback)");
    private int fftInputIndex;
    private double heldPeak;
    private double loudnessDb = FloorDb;
    private double truePeakDb = FloorDb;
    private double? phaseCorrelation;
    private volatile bool isCapturing;
    private volatile InputMixMode mixMode = InputMixMode.AutoMix;
    private double windowsTrimDb;
    private double vstTrimDb;

    public bool IsCapturing => isCapturing;
    public InputMixMode MixMode => mixMode;
    public double WindowsTrimDb => Volatile.Read(ref windowsTrimDb);
    public double VstTrimDb => Volatile.Read(ref vstTrimDb);

    public AudioAnalyzer()
    {
        vstReceiver.SamplesAvailable += OnVstSamplesAvailable;
        vstReceiver.Start();
        mixerThread = new Thread(MixerLoop)
        {
            IsBackground = true,
            Name = "Taskbar Audio mixer"
        };
        mixerThread.Start();
    }

    public AudioSourceInfo SelectedSource
    {
        get
        {
            lock (lifecycleGate)
            {
                return selectedSource;
            }
        }
    }

    public void ConfigureSource(AudioSourceKind kind, string? deviceId, string? displayName)
    {
        lock (lifecycleGate)
        {
            selectedSource = new AudioSourceInfo(
                kind,
                deviceId,
                displayName ?? (kind == AudioSourceKind.DefaultPlaybackLoopback
                    ? "Default playback (loopback)"
                    : "Saved audio device"));
        }
    }

    public void ConfigureMixMode(InputMixMode mode)
    {
        mixMode = mode;
        windowsQueue.Clear();
        vstQueue.Clear();
        ResetMetrics();
    }

    public void ConfigureInputTrims(double newWindowsTrimDb, double newVstTrimDb)
    {
        Volatile.Write(ref windowsTrimDb, Math.Clamp(newWindowsTrimDb, -24, 12));
        Volatile.Write(ref vstTrimDb, Math.Clamp(newVstTrimDb, -24, 12));
    }

    public void SelectSource(AudioSourceInfo source)
    {
        lock (lifecycleGate)
        {
            selectedSource = source;
            isCapturing = false;
            DisposeCapture();
            windowsQueue.Clear();
            windowsResampler.Reset();
            ResetMetrics();
        }

        TryStart();
    }

    public static IReadOnlyList<AudioSourceInfo> GetAvailableSources()
    {
        var sources = new List<AudioSourceInfo>
        {
            new(AudioSourceKind.DefaultPlaybackLoopback, null, "Default playback (loopback)")
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                sources.Add(new AudioSourceInfo(AudioSourceKind.PlaybackLoopback, device.ID, device.FriendlyName));
            }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                sources.Add(new AudioSourceInfo(AudioSourceKind.RecordingInput, device.ID, device.FriendlyName));
            }
        }
        catch
        {
            // The default item remains usable if device enumeration fails temporarily.
        }

        return sources;
    }

    public bool TryStart()
    {
        lock (lifecycleGate)
        {
            if (isCapturing)
            {
                return true;
            }

            DisposeCapture();

            try
            {
                var newCapture = CreateCapture();
                waveFormat = newCapture.WaveFormat;
                capture = newCapture;
                newCapture.DataAvailable += OnDataAvailable;
                newCapture.RecordingStopped += OnRecordingStopped;
                newCapture.StartRecording();
                isCapturing = true;
                return true;
            }
            catch
            {
                DisposeCapture();
                isCapturing = false;
                return false;
            }
        }
    }

    private IWaveIn CreateCapture()
    {
        if (selectedSource.Kind == AudioSourceKind.DefaultPlaybackLoopback)
        {
            return new WasapiLoopbackCapture();
        }

        if (string.IsNullOrWhiteSpace(selectedSource.DeviceId))
        {
            throw new InvalidOperationException("The selected audio device has no endpoint ID.");
        }

        using var enumerator = new MMDeviceEnumerator();
        captureDevice = enumerator.GetDevice(selectedSource.DeviceId);
        return selectedSource.Kind == AudioSourceKind.PlaybackLoopback
            ? new WasapiLoopbackCapture(captureDevice)
            : new WasapiCapture(captureDevice);
    }

    public AnalyzerSnapshot GetSnapshot()
    {
        lock (metricsGate)
        {
            return new AnalyzerSnapshot(
                loudnessDb,
                truePeakDb,
                phaseCorrelation,
                isCapturing,
                vstReceiver.IsConnected,
                mixMode,
                [.. spectrum]);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var format = waveFormat;
        if (format is null || e.BytesRecorded <= 0)
        {
            return;
        }

        var bytesPerSample = format.BitsPerSample / 8;
        var channels = Math.Max(1, format.Channels);
        var frameSize = bytesPerSample * channels;
        if (bytesPerSample <= 0 || frameSize <= 0)
        {
            return;
        }

        var frameCount = e.BytesRecorded / frameSize;
        if (frameCount <= 0)
        {
            return;
        }

        var stereoSamples = ArrayPool<float>.Shared.Rent(frameCount * 2);
        try
        {
            for (var frame = 0; frame < frameCount; frame++)
            {
                var frameOffset = frame * frameSize;
                var left = Math.Clamp(ReadSample(e.Buffer, frameOffset, format), -4f, 4f);
                var right = channels > 1
                    ? Math.Clamp(ReadSample(e.Buffer, frameOffset + bytesPerSample, format), -4f, 4f)
                    : left;
                stereoSamples[frame * 2] = left;
                stereoSamples[frame * 2 + 1] = right;
            }

            var resampled = windowsResampler.Process(
                stereoSamples.AsSpan(0, frameCount * 2),
                format.SampleRate);
            if (resampled.Length > 0)
            {
                windowsQueue.Write(resampled);
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(stereoSamples);
        }
    }

    private void OnVstSamplesAvailable(float[] stereoSamples, int sampleRate)
    {
        var resampled = vstResampler.Process(stereoSamples, sampleRate);
        if (resampled.Length > 0)
        {
            vstQueue.Write(resampled);
        }
    }

    private void MixerLoop()
    {
        var windowsBlock = new float[MixerBlockFrames * 2];
        var vstBlock = new float[MixerBlockFrames * 2];
        var mixedBlock = new float[MixerBlockFrames * 2];
        var blockTicks = Stopwatch.Frequency * MixerBlockFrames / MixerSampleRate;
        var nextBlockAt = Stopwatch.GetTimestamp();

        while (!mixerCancellation.IsCancellationRequested)
        {
            nextBlockAt += blockTicks;
            Array.Clear(windowsBlock);
            Array.Clear(vstBlock);
            Array.Clear(mixedBlock);

            var currentMode = mixMode;
            var windowsFrames = currentMode == InputMixMode.VstOnly
                ? 0
                : windowsQueue.Read(windowsBlock, MixerBlockFrames);
            var vstFrames = currentMode == InputMixMode.WindowsOnly
                ? 0
                : vstQueue.Read(vstBlock, MixerBlockFrames);

            if (windowsFrames > 0 || vstFrames > 0)
            {
                var windowsGain = DecibelsToGain(WindowsTrimDb);
                var vstGain = DecibelsToGain(VstTrimDb);
                for (var sample = 0; sample < mixedBlock.Length; sample++)
                {
                    var mixedSample = (float)(
                        windowsBlock[sample] * windowsGain +
                        vstBlock[sample] * vstGain);
                    mixedBlock[sample] = float.IsFinite(mixedSample) ? mixedSample : 0;
                }

            }

            // Processing zero-filled blocks keeps the exact three-second loudness
            // window and filter tails advancing when an input pauses or disconnects.
            ProcessAudioBlock(mixedBlock, 2, MixerBlockFrames, MixerSampleRate);

            var remainingTicks = nextBlockAt - Stopwatch.GetTimestamp();
            if (remainingTicks > 0)
            {
                var waitMilliseconds = Math.Max(1, (int)(remainingTicks * 1000 / Stopwatch.Frequency));
                mixerCancellation.Token.WaitHandle.WaitOne(waitMilliseconds);
            }
            else if (remainingTicks < -Stopwatch.Frequency / 10)
            {
                nextBlockAt = Stopwatch.GetTimestamp();
            }
        }
    }

    private void ProcessAudioBlock(float[] channelSamples, int channels, int frameCount, int sampleRate)
    {
        lock (analysisGate)
        {
            double blockPeak = 0;
            double leftSquares = 0;
            double rightSquares = 0;
            double leftRightProducts = 0;

            for (var frame = 0; frame < frameCount; frame++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = channelSamples[frame * channels + channel];
                    blockPeak = Math.Max(blockPeak, Math.Abs(sample));
                }

                var left = channelSamples[frame * channels];
                var right = channelSamples[frame * channels + 1];
                shortTermLoudnessMeter.ProcessFrame(left, right);
                leftSquares += left * left;
                rightSquares += right * right;
                leftRightProducts += left * right;
            }

            var spectrumChannel = rightSquares > leftSquares ? 1 : 0;
            for (var frame = 0; frame < frameCount; frame++)
            {
                PushSpectrumSample(channelSamples[frame * channels + spectrumChannel], sampleRate);
            }

            blockPeak = Math.Max(blockPeak, EstimateInterpolatedPeak(channelSamples, channels, frameCount));

            var blockSeconds = (double)frameCount / sampleRate;
            var peakDecay = Math.Exp(-blockSeconds / 1.5);
            double? blockPhaseCorrelation = null;
            var phaseDenominator = Math.Sqrt(leftSquares * rightSquares);
            if (phaseDenominator > 1e-12)
            {
                blockPhaseCorrelation = Math.Clamp(leftRightProducts / phaseDenominator, -1, 1);
            }

            lock (metricsGate)
            {
                heldPeak = Math.Max(blockPeak, heldPeak * peakDecay);
                loudnessDb = shortTermLoudnessMeter.LoudnessLufs;
                truePeakDb = ToDecibels(heldPeak);
                if (blockPhaseCorrelation is double blockPhase)
                {
                    var phaseDecay = Math.Exp(-blockSeconds / PhaseSmoothingSeconds);
                    phaseCorrelation = phaseCorrelation is double previousPhase
                        ? phaseDecay * previousPhase + (1 - phaseDecay) * blockPhase
                        : blockPhase;
                }
                else
                {
                    phaseCorrelation = null;
                }

            }
        }
    }

    private void PushSpectrumSample(float sample, int sampleRate)
    {
        fftInput[fftInputIndex++] = sample;
        if (fftInputIndex < FftLength)
        {
            return;
        }

        fftInputIndex = 0;
        for (var i = 0; i < FftLength; i++)
        {
            fftBuffer[i].X = (float)(fftInput[i] * FastFourierTransform.HammingWindow(i, FftLength));
            fftBuffer[i].Y = 0;
        }

        FastFourierTransform.FFT(true, FftExponent, fftBuffer);
        UpdateSpectrum(sampleRate);
    }

    private void UpdateSpectrum(int sampleRate)
    {
        const double minimumFrequency = 60;
        var maximumFrequency = Math.Min(16000, sampleRate / 2.0);
        var nextSpectrum = new float[SpectrumBarCount];

        for (var band = 0; band < SpectrumBarCount; band++)
        {
            var low = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, (double)band / SpectrumBarCount);
            var high = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, (double)(band + 1) / SpectrumBarCount);
            var lowBin = Math.Clamp((int)Math.Floor(low * FftLength / sampleRate), 1, FftLength / 2 - 1);
            var highBin = Math.Clamp((int)Math.Ceiling(high * FftLength / sampleRate), lowBin + 1, FftLength / 2);
            double magnitude = 0;

            for (var bin = lowBin; bin < highBin; bin++)
            {
                var value = Math.Sqrt(fftBuffer[bin].X * fftBuffer[bin].X + fftBuffer[bin].Y * fftBuffer[bin].Y);
                // NAudio's forward FFT already applies 1/N scaling. A real sine's
                // positive-frequency bin is A/2, then reduced by the Hamming window's
                // coherent gain (approximately 0.54), so undo only those two factors.
                magnitude = Math.Max(magnitude, value * 2 / 0.54);
            }

            var db = ToDecibels(magnitude);
            nextSpectrum[band] = (float)Math.Clamp((db + 72) / 72, 0, 1);
        }

        lock (metricsGate)
        {
            for (var i = 0; i < SpectrumBarCount; i++)
            {
                var blend = nextSpectrum[i] > spectrum[i] ? 0.65f : 0.18f;
                spectrum[i] += (nextSpectrum[i] - spectrum[i]) * blend;
            }
        }
    }

    private static double EstimateInterpolatedPeak(float[] samples, int channels, int frameCount)
    {
        if (frameCount < 4)
        {
            return 0;
        }

        double peak = 0;
        for (var channel = 0; channel < channels; channel++)
        {
            for (var frame = 1; frame < frameCount - 2; frame++)
            {
                var p0 = samples[(frame - 1) * channels + channel];
                var p1 = samples[frame * channels + channel];
                var p2 = samples[(frame + 1) * channels + channel];
                var p3 = samples[(frame + 2) * channels + channel];

                for (var step = 1; step < 4; step++)
                {
                    var t = step / 4.0;
                    var t2 = t * t;
                    var t3 = t2 * t;
                    var interpolated = 0.5 * ((2 * p1) + (-p0 + p2) * t +
                        (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                        (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
                    peak = Math.Max(peak, Math.Abs(interpolated));
                }
            }
        }

        return peak;
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        return format.BitsPerSample switch
        {
            32 when format.Encoding is WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible
                => BitConverter.Int32BitsToSingle(BitConverter.ToInt32(buffer, offset)),
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            24 => Read24BitSample(buffer, offset) / 8388608f,
            16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            8 => (buffer[offset] - 128) / 128f,
            _ => 0
        };
    }

    private static int Read24BitSample(byte[] buffer, int offset)
    {
        var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    private static double ToDecibels(double amplitude)
        => amplitude > 0.0000316228 ? Math.Max(FloorDb, 20 * Math.Log10(amplitude)) : FloorDb;

    private static double DecibelsToGain(double decibels)
        => Math.Pow(10, decibels / 20);

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) => isCapturing = false;

    private void ResetMetrics()
    {
        lock (analysisGate)
        {
            lock (metricsGate)
            {
                fftInputIndex = 0;
                shortTermLoudnessMeter.Reset();
                heldPeak = 0;
                loudnessDb = FloorDb;
                truePeakDb = FloorDb;
                phaseCorrelation = null;
                Array.Clear(spectrum);
                Array.Clear(fftInput);
            }
        }
    }

    public void Dispose()
    {
        lock (lifecycleGate)
        {
            isCapturing = false;
            DisposeCapture();
        }

        vstReceiver.SamplesAvailable -= OnVstSamplesAvailable;
        vstReceiver.Dispose();
        mixerCancellation.Cancel();
        if (mixerThread.IsAlive)
        {
            mixerThread.Join(1000);
        }

        mixerCancellation.Dispose();
    }

    private void DisposeCapture()
    {
        if (capture is null)
        {
            return;
        }

        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        try
        {
            capture.StopRecording();
        }
        catch
        {
            // The device may already have disappeared.
        }

        capture.Dispose();
        capture = null;
        captureDevice?.Dispose();
        captureDevice = null;
        waveFormat = null;
    }
}
