# Testing

Three test projects cover different layers. All use xUnit with FluentAssertions.

## Projects

### BodyCam.Tests (Unit)

Fast, no external dependencies. Mocks all interfaces.

```
src/BodyCam.Tests/
  Agents/           # Agent behavior tests
  Models/           # SessionContext, TranscriptEntry tests
  Mvvm/             # ObservableObject, RelayCommand tests
  Orchestration/    # AgentOrchestrator tests
  Services/         # Service tests with mocks
  Tools/            # Tool execution tests
  ViewModels/       # ViewModel logic tests
  AppSettingsTests.cs
  ModelOptionsTests.cs
```

### BodyCam.IntegrationTests

Tests that wire up multiple real components (but still no external API calls).

```
src/BodyCam.IntegrationTests/
  Fixtures/         # Shared test fixtures
  Orchestration/    # Multi-agent integration tests
  Services/         # Service integration tests
```

### BodyCam.RealTests

Tests against live OpenAI / Azure OpenAI APIs. **Require API keys** configured via `.env` or environment variables. Not run in CI.

```
src/BodyCam.RealTests/
  AzureConnectionTests.cs   # Azure endpoint connectivity
  EventTracking/            # Realtime API event verification
  Fixtures/                 # API client fixtures
  Pipeline/                 # End-to-end pipeline tests
```

## Running Tests

```bash
# Unit tests (fast, no keys needed)
dotnet test src/BodyCam.Tests

# Integration tests
dotnet test src/BodyCam.IntegrationTests

# Real API tests (requires keys)
dotnet test src/BodyCam.RealTests

# Specific test class
dotnet test src/BodyCam.RealTests --filter "FullyQualifiedName~ToolRegistrationTests"
```

## Conventions

- Test classes mirror the source structure (e.g., `Tools/FindObjectToolTests.cs` tests `Tools/FindObjectTool.cs`)
- Use `FluentAssertions` for assertions (`result.Should().Be(...)`)
- Mock interfaces with `NSubstitute` or manual fakes
- Real tests are filtered by class name to avoid running the full suite

## Real Hardware Tests

Some tests require physical hardware and are gated by environment variables:

### HeyCyan Glasses Latency Benchmarks

Located in `BodyCam.RealTests/HeyCyanCameraLatencyTests.cs`. These tests measure camera capture latency on real HeyCyan smart glasses.

**Requirements:**
- HeyCyan glasses paired and powered on
- Android device or emulator with glasses connected
- `BODYCAM_REAL_HEYCYAN=1` environment variable
- `BODYCAM_REAL_HEYCYAN_MAC=XX:XX:XX:XX:XX:XX` environment variable

**Running:**

```powershell
# PowerShell script (sets environment variables automatically)
.\src\BodyCam.RealTests\run-heycyan-latency.ps1 -Mac "A1:B2:C3:D4:E5:F6"

# Or manually
$env:BODYCAM_REAL_HEYCYAN = '1'
$env:BODYCAM_REAL_HEYCYAN_MAC = 'A1:B2:C3:D4:E5:F6'
dotnet test src\BodyCam.RealTests --filter "FullyQualifiedName~HeyCyanCameraLatencyTests"
```

**Tests:**
- `CaptureFrameAsync_ColdLatency_IsUnderSixSeconds` — Cold capture (after idle) must complete in ≤ 6s
- `CaptureFrameAsync_WarmLatency_IsUnderTwoSeconds` — Warm capture (within 8s window) must complete in ≤ 2s
- `CaptureFrameAsync_LatencyDistribution_RecordedToCsv` — Runs 10 cold + 10 warm captures, writes percentiles to `TestResults/heycyan-latency.csv`

**CI Configuration:**

When CI workflows are added, real hardware tests must be excluded from the default test run:

```yaml
# Example .github/workflows/test.yml
- name: Run unit tests
  run: dotnet test src/BodyCam.Tests --filter "Category!=RealHardware"

- name: Run integration tests
  run: dotnet test src/BodyCam.IntegrationTests --filter "Category!=RealHardware"

# Real tests skipped — they require hardware
```

A separate manual-dispatch workflow can be added for real hardware tests when needed.

