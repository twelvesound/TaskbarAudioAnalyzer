# Taskbar Audio Analyzer

[日本語](README.md) | [English](README.en.md)

A lightweight resident audio analyzer designed to sit behind a transparent Windows taskbar.

Meters:

- `LUFS-S`: Short-Term LUFS using ITU-R BS.1770 K-weighting and the EBU Tech 3341 three-second window. Updated five times per second with a 0.2 LU deadband
- `TP`: Lightweight true-peak estimate using 4x interpolation, displayed in dBFS
- `PHASE`: Left/right phase correlation from −1 to +1, smoothed over approximately 400 ms and updated ten times per second
- Spectrum: 28 logarithmically spaced bands covering approximately 60 Hz to 16 kHz
- Input status: `WIN`, `VST`, or `WIN+VST` while using Auto Mix

## Getting started

```powershell
git clone https://github.com/twelvesound/TaskbarAudioAnalyzer.git
cd TaskbarAudioAnalyzer
.\scripts\Start-Analyzer.ps1
```

Requirements: Windows 10 or later and the .NET 10 SDK.

## Controls

- Left-drag the analyzer to move it.
- Right-click the notification-area icon and select `Audio source` to choose the audio being analyzed:
  - `Default playback (loopback)`: The current default Windows playback device
  - `Playback devices (loopback)`: The output of a specific playback device
  - `Recording inputs`: A microphone, audio interface, or loopback recording input
- Open `Input mode` from the notification-area menu:
  - `Auto Mix (Windows + VST)`: Automatically combines Windows audio and the VST3 tap; this is the default
  - `Windows only`: Analyzes only the selected Windows audio source
  - `VST only`: Analyzes only the VST3 tap
  - `Windows trim` / `VST trim`: Adjusts each input from −12 to +6 dB
- Select `Enable startup` to launch the analyzer automatically when Windows starts.
- Select `Disable startup` to remove automatic startup.
- Select `Exit` to close the analyzer.

## Notes

- The default source is the current playback device captured through WASAPI loopback. Other playback and recording devices can be selected from the notification-area menu.
- If an audio device becomes temporarily unavailable, the analyzer retries the connection every two seconds.
- The selected audio source is saved together with the window position.
- Input mode and input trim values are also saved.
- Settings are stored in `%LOCALAPPDATA%\12sound\TaskbarAudioAnalyzer\settings.json`. Settings left in a pre-reorganization build folder are migrated automatically on first launch.
- When startup is enabled, a normal launch refreshes the registered executable path to the current location.
- `LUFS-S` measures stereo audio normalized to 48 kHz using BS.1770 K-weighting. Input trims and Auto Mix summing are applied before measurement.
- `TP` is a lightweight 4x-interpolated estimate and is not a substitute for a certified meter.
- On first launch, the window is placed at the bottom center of the primary display. Its position is saved after it is dragged.

## ASIO / VST3 tap

`Taskbar Audio Tap` is a pass-through VST3 plug-in. It does not modify the signal; it sends stereo PCM to the analyzer through shared memory.

Building the VST3 requires CMake, the Visual Studio 2026 C++ build tools, and the Steinberg VST3 SDK. Clone the SDK into the expected directory:

```powershell
git clone --recursive https://github.com/steinbergmedia/vst3sdk.git .\external\vst3-sdk
```

Build the plug-in and install it into the system VST3 directory from an Administrator PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Vst3.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Install-Vst3.ps1
```

The plug-in is installed at `C:\Program Files\Common Files\VST3\12sound\TaskbarAudioTap.vst3`, and its registered vendor name is `12sound`.

Rescan plug-ins in your DAW and insert one `Taskbar Audio Tap` instance as the final plug-in on the master output. Set the analyzer's `Input mode` to `Auto Mix (Windows + VST)` to analyze Windows playback and ASIO-routed DAW audio together without manually switching sources.

If taps are active in multiple DAWs or on multiple tracks, only the first instance that starts sending audio is used. When that instance stops for approximately one second, ownership passes automatically to the next active tap.

The mixer does not apply limiting or automatic normalization. If the combined signal exceeds 0 dBFS, the TP readout shows the overage as-is.

## Directory layout

```text
TaskbarAudioAnalyzer/
├─ src/
│  ├─ TaskbarAudioAnalyzer/   # WPF application
│  └─ TaskbarAudioTap/        # VST3 plug-in
├─ scripts/                   # Start, build, and install scripts
├─ external/                  # External SDKs
├─ tools/                     # Local development tools
└─ artifacts/                 # Build products and generated files
```

To build only the WPF application:

```powershell
dotnet build .\src\TaskbarAudioAnalyzer\TaskbarAudioAnalyzer.csproj
```

## License

This project is released under the [MIT License](LICENSE). You may use, modify, and redistribute it for commercial or non-commercial purposes as long as the copyright notice and license text are retained.
