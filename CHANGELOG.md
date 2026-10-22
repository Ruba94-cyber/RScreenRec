# Changelog

## Unreleased

- Added opt-in selected-window recording with Windows Graphics Capture and a warned PrintWindow compatibility fallback.
- Preserve the normal monitor start/second-launch stop workflow; report updated and repeated source frames.
- Use a 30 FPS H.264 quality profile tuned to retain screen text while keeping typical UI recordings compact.
- Keep the last complete selected-window image in the MP4 timeline while a target is minimized, then resume on restore.
- Added validation notes for unusual dimensions, occlusion, resize/move, minimize/restore, MP4 decoding, and long timelines.
- Added a Windows smoke runner for picker selection, synthetic WGC formats, occlusion, restore, source closure, MP4 decoding, and over-one-hour timeline checks.
