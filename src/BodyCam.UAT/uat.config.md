# UAT Config

## Runtime

| Field | Value |
| --- | --- |
| Target | MAUI |
| Fixture | Appium |
| AppPath | ../BodyCam/bin/Debug/net10.0-windows10.0.19041.0/win-x64/BodyCam.exe |
| WorkingDirectory | ../.. |

## Assemblies

| Kind | Assembly |
| --- | --- |
| Pages | BodyCam.UITestKit.dll |
| Controls | Brinell.Maui.dll |
| Commands | Brinell.Uat.dll |

## Discovery

| Field | Value |
| --- | --- |
| RequireExplicitUatAttributes | false |
| AllowNameInference | true |

## Reporting

| Field | Value |
| --- | --- |
| ScreenshotOnFailure | true |
| IncludeRuntimeTrace | true |

## Skip Rules

| Tag | EnvironmentVariable |
| --- | --- |
| hardware | BODYCAM_UAT_HARDWARE |
| live-api | BODYCAM_UAT_LIVE_API |
| manual | BODYCAM_UAT_MANUAL |
| semi-automated | BODYCAM_UAT_SEMI_AUTOMATED |
