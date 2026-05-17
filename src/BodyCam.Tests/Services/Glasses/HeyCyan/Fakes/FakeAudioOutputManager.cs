namespace BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;

/// <summary>
/// Minimal fake of AudioOutputManager for testing HeyCyanAudioRouter.
/// Tracks SetActiveProviderAsync calls and the current active provider ID.
/// </summary>
public sealed class FakeAudioOutputManager
{
    private readonly List<string> _history = new();

    public FakeAudioOutputManager(string initialProviderId)
    {
        ActiveProviderId = initialProviderId;
    }

    /// <summary>
    /// The currently active audio output provider ID.
    /// </summary>
    public string ActiveProviderId { get; private set; }

    /// <summary>
    /// History of all SetActiveProviderAsync calls, for test assertions.
    /// </summary>
    public IReadOnlyList<string> History => _history.AsReadOnly();

    /// <summary>
    /// Simulate setting the active provider. Records the call in History.
    /// </summary>
    public Task SetActiveProviderAsync(string providerId, CancellationToken ct = default)
    {
        ActiveProviderId = providerId;
        _history.Add(providerId);
        return Task.CompletedTask;
    }
}
