# Selected-window recording

Selected-window recording is opt-in and leaves the normal monitor workflow unchanged.

## Try it

1. Launch RScreenRec while holding **Shift**.
2. Select a visible, non-minimized window.
3. Use other applications while recording. Window mode captures the selected HWND only and does not include other desktop windows.
4. Launch RScreenRec again to stop and finalize the MP4.

WGC is the preferred source on Windows 10 version 1803 or later. If WGC cannot initialize, the application displays a warning and tries PrintWindow. PrintWindow may return black or incomplete content for GPU-rendered, protected, layered, and special windows. Neither backend silently switches to a desktop recording.

## Minimize and restore

Windows may stop rendering a minimized window. RScreenRec holds its last captured frame in the 30 FPS timeline, counts those repeated samples separately, and resumes with new content after restore. This behavior does not provide live updates while minimized. Restore the target whenever live content needs to be recorded.

## Validation status

The Windows smoke run used synthetic WinForms windows at odd landscape, portrait, and panoramic dimensions. It verified WGC capture, absence of an unrelated magenta occluder, resize and move handling, minimize/restore behavior, even-dimension H.264 output, 30 FPS metadata, and full-file decoding. The actual executable also completed monitor-mode start/second-launch stop against a synthetic full-screen backdrop: the finalized file was 1,283,304 bytes, 2560x1600, 30 FPS, 130 frames over 4.333 seconds, decoded without errors, and left no RScreenRec process running. A separate monitor-source smoke capture and an accelerated 3601-second writer timeline also decoded successfully.

The automated harness opens the real window-picker dialog and selects a separate synthetic target process, then exercises WGC capture and MP4 validation. A separate close test closes the selected target during capture; the source-close error was reported, the MP4 finalized at 30 FPS with 67 frames over 2.233 seconds, and full decode succeeded. The packaged executable's Shift-key launch path has not yet been verified end-to-end. Browser, hardware-accelerated, protected-content, mixed-DPI multi-monitor, real notification, and real one-hour interactive recording checks also remain outstanding.

Browser, hardware-accelerated, protected-content, mixed-DPI multi-monitor, real notification, and real one-hour interactive recording checks have not been completed. Keep the feature in its Draft PR until the target-app and endurance checks pass. See [the implementation and test plan](window-capture-plan.md) for details.
