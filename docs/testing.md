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
