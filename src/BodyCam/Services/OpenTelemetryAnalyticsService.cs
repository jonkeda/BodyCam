using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BodyCam.Services;

public class OpenTelemetryAnalyticsService : IAnalyticsService
{
    private static readonly ActivitySource Source = new("BodyCam.Analytics");
    private static readonly Meter AnalyticsMeter = new("BodyCam.Analytics");
    private static readonly Counter<long> EventCounter = AnalyticsMeter.CreateCounter<long>("bodycam.events");

    private readonly ISettingsService _settings;

    public bool IsEnabled => _settings.SendUsageData;

    public OpenTelemetryAnalyticsService(ISettingsService settings)
    {
        _settings = settings;
    }

    public void TrackEvent(string name, IDictionary<string, string>? properties = null)
    {
        if (!IsEnabled) return;

        using var activity = Source.StartActivity(name, ActivityKind.Internal);
        if (activity is not null && properties is not null)
        {
            foreach (var (key, value) in properties)
                activity.SetTag(key, value);
        }

        EventCounter.Add(1, new KeyValuePair<string, object?>("event.name", name));
    }

    public void TrackMetric(string name, double value, IDictionary<string, string>? tags = null)
    {
        if (!IsEnabled) return;

        using var activity = Source.StartActivity($"metric.{name}", ActivityKind.Internal);
        activity?.SetTag("metric.value", value);
        if (tags is not null)
        {
            foreach (var (key, val) in tags)
                activity?.SetTag(key, val);
        }
    }
}
