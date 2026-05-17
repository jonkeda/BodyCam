namespace BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;

/// <summary>
/// Minimal fake of AudioInputManager for testing HeyCyanAudioRouter.
/// Tracks SetActiveProviderAsync calls and the current active provider ID.
/// </summary>
public sealed class FakeAudioInputManager
{
    private readonly List<string> _history = new();

    public FakeAudioInputManager(string initialProviderId)
    {
        ActiveProviderId = initialProviderId;
    }

    /// <summary>
    /// The currently active audio input provider ID.
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
