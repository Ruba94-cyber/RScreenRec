# Window capture implementation and review

## Scope
- Preserve normal launch: first invocation records the cursor monitor; second invocation stops.
- Holding Shift during first invocation opts into window selection.
- Capture only the selected window, without desktop notifications or unrelated applications.
- Allow users to restore and interact with the target window at any time.
- Never claim live minimized capture unless validated for the target application.
- No application-specific identifiers or behaviors.

## Current state
The Shift modifier is detected in Program.cs. Until a separate window-capture backend and picker exist, Shift mode exits with an explicit message instead of silently recording the desktop.

## Implementation tasks
1. Introduce an abstract frame source (screen and window) while retaining the existing Media Foundation writer.
2. Build a nonintrusive window picker and reject invalid, protected, or closed windows.
3. Use Windows Graphics Capture on supported Windows versions, with explicit unsupported-OS handling. The current README claims Windows 7 compatibility for ordinary recording.
4. Handle target window close, resize, DPI changes, multi-monitor movement, and capture interruption.
5. Detect lack of fresh frames while minimized; report the limitation instead of presenting repeated frames as live activity.
6. Verify pointer/touch overlays are positioned in window-relative coordinates.
7. Preserve second-launch stop semantics, recording indicator, file naming, and single-executable packaging.
8. Add automated tests and Windows integration tests.

## Static-review findings to investigate
- ScreenRecorder.StopRecording clears recording state and frame buffer after a 90-second Join timeout even if the capture thread remains alive; ownership and disposal must be made safe.
- StartRecording returns before the recording thread writes its first frame; startup errors can be asynchronous.
- The stop-request file uses a global temporary filename and can race across processes or user sessions.
- The catch-up loop repeats the last frame; this is valid for timing but must not be confused with live target updates.
- TouchOverlayForm converts screen touch coordinates to client coordinates, then publishes those client points; ScreenRecorder.DrawTouchPointers uses them without a corresponding screen-to-capture transform.
- Verify overlays, stop event cleanup, DPI fallbacks, and encoding finalization under failure conditions.

## Acceptance tests
- Normal first/second launch behavior unchanged.
- Shift launches window selection without writing desktop pixels.
- Switching to unrelated apps and receiving notifications does not affect captured window content.
- Target restoration and focus work while capture continues.
- Minimized behavior is explicitly verified and accurately reported.
- No leaked capture resources after closing the target or stopping recording.
