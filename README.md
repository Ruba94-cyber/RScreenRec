# RScreenRec - Advanced Screen Recording Tool

A C# desktop application for advanced screen recording with visual overlays and touch input support.

Author: Paolo Rubagotti.

## Features

### 🎥 Screen Recording
- **Multi-monitor aware**: Automatically records the monitor under the mouse cursor.
- **Screen-format safe**: Supports standard, ultrawide, portrait, 16:10, 4:3, and odd pixel dimensions without cropping.
- **Lightweight high quality capture**: 30 FPS MP4 recording with native Windows H.264 encoding tuned for small screen-recording files.
- **Small files**: Around 3-4 MB/minute on a 1920x1200 desktop in typical screen-recording use.
- **DPI aware**: Optimized for high-resolution displays (e.g., Panasonic FG-Z2).
- **Portable single executable**: No FFmpeg, codecs, or helper binaries are shipped beside the app.
- **Automatic file management**: Timestamped filenames avoid slow startup scans.

### 🎯 Visual Overlays
- **Recording indicator**: Blinking dot to show active recording.
- **Mouse cursor overlay**: Highlights the cursor in recordings with a red dot.
- **Touch overlay**: Displays touch input using red, outlined circles.

### 🔧 Technical Capabilities
- **File locking**: Prevents multiple instances from running simultaneously.
- **Thread-safe design**: Safe coordination of overlay and recording threads.
- **Robust error handling**: Comprehensive exception management.
- **Performance tuned**: Minimal allocations and efficient resource usage.

## System Requirements

- **Operating System**: Windows 7 or later
- **.NET Framework**: 4.8
- **Hardware**: DirectX-compatible graphics adapter for screen capture
- **Touch**: Optional Windows Touch support for the touch overlay

## Usage

### Quick Start
```bash
RScreenRec.exe
```

### How it works
1. **First launch**: Detects the current monitor and starts recording immediately.
2. **Second launch**: Stops the current recording if one is running.
3. **Output files**: Videos are saved to `%USERPROFILE%\Videos\Captures\`.

### File Naming
Files follow the pattern `rec_[timestamp].mp4`:
- **timestamp**: Uses the format `yyyy-MM-dd_HH-mm-ss-fff`.

Example: `rec_2026-06-04_16-22-40-234.mp4`

## Code Architecture

### Key Components

#### `Program.cs`
- Application entry point.
- Manages the lock file used to prevent multiple instances.
- Coordinates threads used for overlays.
- Controls the application lifecycle.

#### `ScreenRecorder.cs`
- Core engine responsible for screen capture.
- Generates MP4 files through the native Media Foundation writer.
- Thread-safe management of recording loops.
- Integrates mouse cursor overlay.

#### `MediaFoundationMp4Writer.cs`
- Thin COM/PInvoke wrapper around Windows Media Foundation.
- Writes H.264 video into an MP4 container.
- Uses explicit frame timestamps and durations for normal playback speed.

#### `RecordingOverlayForm.cs`
- Transparent overlay indicating active recording.
- Always-on-top window with custom blinking animation.
- Automatically positioned on the screen.

#### `TouchOverlayForm.cs`
- Captures and renders touch input events.
- Displays full-screen transparent overlay.
- Cleans up visual touch indicators automatically.

## Advanced Configuration

### Performance Tuning
- **FPS**: Adjustable in `ScreenRecorder.cs` around line 21.
- **Quality/size**: Uses compact screen-recording bitrate presets in `ScreenRecorder.cs`.
- **Buffer size**: Automatically determined based on resolution.

### Overlay Customization
- **Indicator position**: Configurable in `RecordingOverlayForm.cs` near lines 19-20.
- **Colors**: Modify the overlay classes to adjust colors.
- **Sizes**: Change constructor parameters to tweak overlay dimensions.

## Troubleshooting

### Common Issues

**Recording does not start**
- Verify permissions for the output folder.
- Check available disk space.
- Ensure no other instance is running.

**Low performance**
- Close unnecessary applications.
- Monitor CPU and memory usage.
- Reduce screen resolution if needed.

**Overlays not visible**
- Check Windows DPI settings.
- Review multi-monitor configuration.
- Restart with administrator privileges if required.

### Logging
Runtime logs are written to `%TEMP%\screenrec.log`.

## Development

### Build Requirements
- Visual Studio 2019 or later
- .NET Framework 4.8 SDK
- Windows SDK for touch functionality

### Project Structure
```
RScreenRec/
├── Program.cs              # Entry point
├── ScreenRecorder.cs       # Core recording engine
├── MediaFoundationMp4Writer.cs # Native MP4/H.264 writer
├── RecordingOverlayForm.cs # Recording indicator overlay
├── TouchOverlayForm.cs     # Touch input overlay
├── RScreenRec.csproj       # Project configuration
└── README.md               # Documentation
```

### Build Commands
```bash
# Debug build (produces RScreenRec.exe in bin\\Debug)
msbuild RScreenRec.csproj /p:Configuration=Debug

# Release build (produces RScreenRec.exe in bin\\Release)
msbuild RScreenRec.csproj /p:Configuration=Release
```

## License

This project is distributed under the MIT License. © 2025 Paolo Rubagotti. See the `LICENSE` file for details.

## Contributing

Contributions are welcome! To contribute:
1. Fork the repository.
2. Create a feature branch (`git checkout -b feature/new-feature`).
3. Commit your changes with clear messages.
4. Open a pull request describing your changes.

## Support

If RScreenRec helps you work faster, you can buy me a coffee by sending a PayPal donation through the [direct contribution page](https://www.paypal.com/donate?business=paolo.ruba.1913%40gmail.com&no_recurring=0&item_name=Support+RScreenRec&currency_code=EUR).
