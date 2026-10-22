# Experimental selected-window recording

This branch includes an **opt-in compatibility implementation**, not yet the
planned Windows Graphics Capture (WGC) backend.

## How to try it

1. Launch RScreenRec while holding **Shift**.
2. Read and accept the experimental-capture warning.
3. Select a visible, non-minimized window.
4. Launch RScreenRec again to stop the recording, as in monitor mode.

The window capture implementation requests frames from the selected HWND using
`PrintWindow(PW_RENDERFULLCONTENT)`. It does **not** use screen-region capture
as a fallback, and does not create monitor-wide touch/REC overlays.

## Known limitations

- **Not tested on Windows yet.** Compilation and real application testing are
  required before release.
- Some hardware-accelerated, protected, layered, or special windows return black,
  stale, or incomplete frames even if PrintWindow reports success.
- A minimized window keeps the MP4 timeline running with the last captured frame.\n  Live content updates while minimized are not guaranteed; capture resumes on restore.
- The output resolution is fixed at the size selected at startup. Resizing the
  target is handled by fitting its current image inside the original video size,
  preserving aspect ratio and adding black bars when necessary. Very large
  windows are rejected to bound temporary memory use.
- Cursor and touch annotations are disabled for window-only capture to avoid
  incorrect coordinates and overlay contamination.
- PrintWindow can block while the target window renders. Stop may take longer
  for an unresponsive target.
- Notifications and other windows are not captured via desktop pixels, but the
  target app itself controls what PrintWindow renders.
- This compatibility backend does **not** meet the full reliability requirements
  for independent GPU-backed capture. WGC remains the intended implementation.

## Windows test matrix

- Ordinary Win32 window, foreground and covered by another application
- Hardware-accelerated application and browser
- Move target between displays with different DPI settings
- Resize, minimize, restore, and close target while recording
- Second-launch stop, repeated starts, encoder failure, and file integrity
- Confirm unrelated notifications and windows never appear in recorded frames

Do not merge this feature into the default release until the behavior is
verified and the limitations are acceptable.

## Build and acceptance

Run `scripts/build-check.ps1` on a Windows development machine with
Visual Studio Build Tools and the .NET Framework 4.8 targeting pack.

**Current status:** Source changes have been committed, but no Windows build,
automated UI test, or actual MP4 playback test has been executed in this
development environment. The experimental feature must remain in a draft PR.

**Release blockers:** A verified Windows build, capture/playback validation,
GPU-backed app coverage, and an implementation using Windows Graphics Capture
for broader compatibility. No claim is made that minimized apps continue to\nproduce live frames.
