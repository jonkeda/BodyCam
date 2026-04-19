namespace BodyCam.Services;

public interface IAnalyticsService
{
    bool IsEnabled { get; }
    void TrackEvent(string name, IDictionary<string, string>? properties = null);
    void TrackMetric(string name, double value, IDictionary<string, string>? tags = null);
}
