# M47 - Device Media Architecture Report

**Status:** Report created

## Goal

Document how BodyCam currently selects and uses:

- camera pictures;
- camera/video-like capture;
- input audio;
- output audio.

The main question is whether the app has two architectures:

1. the Device settings architecture used by the Devices page;
2. the architecture actually used by the front page and active app session.

## Result

See [Architecture Report](./report.md).

Short answer: there is one shared provider-manager layer, but there are
currently two orchestration paths that can drive it. The front page/runtime path
uses managers and some legacy direct `CameraView` capture. The Devices settings
path uses `SourceProfileManager` and profile JSON settings, but that profile
manager is not the only runtime owner.

## Follow-Up Direction

The recommended next milestone is to choose one owner for source selection.
Either:

- make `SourceProfileManager` the single runtime source-selection owner; or
- make Device settings a pure facade over `CameraManager`,
  `AudioInputManager`, and `AudioOutputManager`.

The report recommends the first option because it matches the user's mental
model: select a source profile, and the app should use that profile everywhere.
