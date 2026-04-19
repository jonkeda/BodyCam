namespace BodyCam.Services;

public class NullAnalyticsService : IAnalyticsService
{
    public bool IsEnabled => false;
    public void TrackEvent(string name, IDictionary<string, string>? properties = null) { }
    public void TrackMetric(string name, double value, IDictionary<string, string>? tags = null) { }
}
