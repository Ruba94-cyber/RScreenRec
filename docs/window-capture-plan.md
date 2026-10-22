# Selected-window capture

## Behavior

- Normal launch still starts monitor recording; a second launch stops and finalizes it.
- Holding Shift opens a picker of visible top-level windows. Canceling the picker does not start recording.
- Windows Graphics Capture (WGC) is the preferred selected-window source. If WGC cannot initialize, RScreenRec warns and attempts PrintWindow compatibility capture.
- Window mode does not draw the monitor-wide recording or touch overlays and never falls back to desktop pixels.
- The output keeps the selected window's startup dimensions. Resized frames are fitted into that canvas while preserving aspect ratio.

## Minimized windows and frame timing

Windows may stop rendering a minimized window. RScreenRec keeps the last complete image in the MP4 timeline while the source is minimized; repeated output frames are counted separately from updated source frames. This is a freeze, not live capture. New frames resume after the window is restored. Closing the target or losing the WGC source stops capture with an error and attempts to finalize the MP4.

## Backend limits

- WGC requires Windows 10 version 1803 or later. Behavior on Windows versions and hardware not tested remains unverified.
- PrintWindow is a compatibility path. GPU-rendered, protected, layered, or unusual applications may return black or incomplete content. A successful PrintWindow call alone does not prove the pixels are correct.
- The Windows build used for this branch selected WGC for a synthetic WinForms window. Browser, protected-content, and hardware-accelerated application coverage remains outstanding.
- WGC capture remains independent of unrelated windows in the synthetic covered-window test. Notifications, multi-monitor mixed-DPI movement, and real GPU applications still require validation on target systems.

## Windows smoke results

On the available Windows environment, synthetic WGC recordings were produced at 671x377, 377x671, and 1001x311. H.264 output padded odd dimensions to even sizes, retained 30 FPS, and decoded successfully. The 671x377 test covered the target with a magenta window, resized and moved it, minimized it, then restored it. The unrelated magenta content was absent; the last complete target frame was held while minimized; content resumed after restore. The resize transition was repeated after correcting a test-harness paint exception and after guarding the WGC frame-pool resize boundary against one partial surface.

A separate monitor-source smoke test recorded synthetic desktop content at 671x377, finalized a 30 FPS MP4, and decoded successfully. A synthetic 3601-second writer run encoded 108,030 frames and decoded with a reported duration of 3601 seconds. This validates the writer's long timestamp timeline; it is not a real 60-minute interactive capture or a CPU/GPU endurance test.

## Remaining release validation

Before removing Draft status, test the picker and second-launch stop flow on target Windows versions; exercise browser and GPU-accelerated windows, target closure, cross-monitor DPI changes, notifications, and multiple start/stop cycles; and measure resource use during a real long recording. Keep the PR in Draft until those environment-dependent checks pass.
