using System.Text.Json;

namespace BodyCam.Services.AiProviders;

public sealed record AiProviderInstanceSettings
{
    public required string InstanceId { get; init; }
    public required string ProviderId { get; init; }
    public required string DisplayName { get; init; }
    public bool IsActive { get; init; }
    public required string CredentialMode { get; init; }
    public Dictionary<string, string> Settings { get; init; } = [];
}

public interface IAiProviderInstanceStore
{
    Task<IReadOnlyList<AiProviderInstanceSettings>> GetInstancesAsync();
    Task<AiProviderInstanceSettings> EnsureInstanceAsync(string providerId);
    Task SetActiveAsync(string instanceId);
    Task RemoveAsync(string instanceId);
}

public sealed class AiProviderInstanceStore : IAiProviderInstanceStore
{
    private const string InstancesPreferenceKey = "AiProviderInstances";
    private static readonly object Gate = new();
    private readonly IAiProviderRegistry _registry;
    private readonly ISettingsService _settings;
    private readonly Func<string?> _loadJson;
    private readonly Action<string> _saveJson;

    public AiProviderInstanceStore(IAiProviderRegistry registry, ISettingsService settings)
        : this(
            registry,
            settings,
            () => Preferences.Get(InstancesPreferenceKey, string.Empty),
            json => Preferences.Set(InstancesPreferenceKey, json))
    {
    }

    internal AiProviderInstanceStore(
        IAiProviderRegistry registry,
        ISettingsService settings,
        Func<string?> loadJson,
        Action<string> saveJson)
    {
        _registry = registry;
        _settings = settings;
        _loadJson = loadJson;
        _saveJson = saveJson;
    }

    public Task<IReadOnlyList<AiProviderInstanceSettings>> GetInstancesAsync()
    {
        lock (Gate)
        {
            var instances = LoadInstances();
            if (instances.Count == 0)
            {
                instances = CreateDefaultInstances();
                SaveInstances(instances);
            }

            return Task.FromResult<IReadOnlyList<AiProviderInstanceSettings>>(ApplyActiveProvider(instances));
        }
    }

    public Task<AiProviderInstanceSettings> EnsureInstanceAsync(string providerId)
    {
        providerId = AiProviderIds.Normalize(providerId);
        lock (Gate)
        {
            var instances = LoadInstances();
            if (instances.Count == 0)
                instances = CreateDefaultInstances();

            var existing = instances.FirstOrDefault(instance => instance.ProviderId == providerId);
            if (existing is not null)
            {
                SaveInstances(instances);
                return Task.FromResult(ApplyActiveProvider([existing])[0]);
            }

            var provider = _registry.GetRequired(providerId);
            var added = CreateInstance(provider);
            instances.Add(added);
            SaveInstances(instances);
            return Task.FromResult(ApplyActiveProvider([added])[0]);
        }
    }

    public Task SetActiveAsync(string instanceId)
    {
        lock (Gate)
        {
            var instances = LoadInstances();
            var instance = instances.FirstOrDefault(item => item.InstanceId == instanceId)
                ?? throw new InvalidOperationException($"LLM provider instance '{instanceId}' was not found.");

            _settings.ProviderId = instance.ProviderId;
            SaveInstances(instances
                .Select(item => item with { IsActive = item.InstanceId == instanceId })
                .ToList());
            return Task.CompletedTask;
        }
    }

    public Task RemoveAsync(string instanceId)
    {
        lock (Gate)
        {
            var instances = LoadInstances();
            instances.RemoveAll(instance => instance.InstanceId == instanceId);
            SaveInstances(instances);
            return Task.CompletedTask;
        }
    }

    private List<AiProviderInstanceSettings> CreateDefaultInstances() =>
        _registry.Providers
            .Where(provider => provider.IsSelectable)
            .Select(CreateInstance)
            .ToList();

    private AiProviderInstanceSettings CreateInstance(AiProviderDefinition provider) =>
        new()
        {
            InstanceId = provider.Id,
            ProviderId = provider.Id,
            DisplayName = provider.DisplayName,
            IsActive = provider.Id == AiProviderIds.Normalize(_settings.ProviderId),
            CredentialMode = provider.CredentialModes.Contains(AiCredentialMode.ApiKey)
                ? AiCredentialMode.ApiKey.ToString()
                : provider.CredentialModes.FirstOrDefault().ToString(),
        };

    private List<AiProviderInstanceSettings> ApplyActiveProvider(List<AiProviderInstanceSettings> instances)
    {
        var activeProviderId = AiProviderIds.Normalize(_settings.ProviderId);
        return instances
            .Select(instance => instance with { IsActive = instance.ProviderId == activeProviderId })
            .ToList();
    }

    private List<AiProviderInstanceSettings> LoadInstances()
    {
        var json = _loadJson();
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<AiProviderInstanceSettings>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveInstances(List<AiProviderInstanceSettings> instances) =>
        _saveJson(JsonSerializer.Serialize(instances));
}
