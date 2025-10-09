# RScreenRec - Advanced Screen Recording Tool

A C# desktop application for advanced screen recording with visual overlays and touch input support.

## Features

### 🎥 Screen Recording
- **Multi-monitor aware**: Automatically records the monitor under the mouse cursor.
- **High quality capture**: 30 FPS recording using uncompressed AVI output.
- **DPI aware**: Optimized for high-resolution displays (e.g., Panasonic FG-Z2).
- **Automatic file management**: Sequential numbering and timestamped filenames.

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
- **.NET Framework**: 4.7.2 or later
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
Files follow the pattern `rec_[number]_[timestamp].avi`:
- **number**: Automatically incremented counter.
- **timestamp**: Uses the format `HHhMMmSSs_dd-MM-yyyy`.

Example: `rec_1_14h30m45s_26-09-2025.avi`

## Code Architecture

### Key Components

#### `Program.cs`
- Application entry point.
- Manages the lock file used to prevent multiple instances.
- Coordinates threads used for overlays.
- Controls the application lifecycle.

#### `ScreenRecorder.cs`
- Core engine responsible for screen capture.
- Generates AVI files through a custom writer.
- Thread-safe management of recording loops.
- Integrates mouse cursor overlay.

#### `AviWriter.cs`
- Custom AVI writer for uncompressed output.
- Handles standard AVI headers and indexing.
- Optimized for real-time streaming.
- Supports RGB 24-bit format.

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
- **Quality**: Uses uncompressed RGB24 video (modifiable in `AviWriter.cs`).
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
Errors are written to the console. For advanced debugging:
```bash
RScreenRec.exe > log.txt 2>&1
```

## Development

### Build Requirements
- Visual Studio 2019 or later
- .NET Framework 4.7.2 SDK
- Windows SDK for touch functionality

### Project Structure
```
RScreenRec/
├── Program.cs              # Entry point
├── ScreenRecorder.cs       # Core recording engine
├── AviWriter.cs            # AVI file writer
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

This project is distributed under the MIT License. See the `LICENSE` file for details.

## Contributing

Contributions are welcome! To contribute:
1. Fork the repository.
2. Create a feature branch (`git checkout -b feature/new-feature`).
3. Commit your changes with clear messages.
4. Open a pull request describing your changes.
