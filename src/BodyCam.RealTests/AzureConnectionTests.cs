using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests;

/// <summary>
/// Real integration tests that hit the live Azure OpenAI endpoint.
/// Reads credentials from the .env file at the repo root.
/// Skip these in CI — they require real Azure credentials.
/// </summary>
public class AzureConnectionTests
{
    private readonly string? _endpoint;
    private readonly string? _apiKey;
    private readonly string? _apiVersion;
    private readonly string? _realtimeDeployment;
    private readonly string? _chatDeployment;
    private readonly string? _visionDeployment;
    private readonly ITestOutputHelper _output;

    public AzureConnectionTests(ITestOutputHelper output)
    {
        _output = output;

        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, ".env");
            if (File.Exists(candidate))
            {
                var vars = ParseEnv(candidate);
                vars.TryGetValue("AZURE_OPENAI_ENDPOINT", out _endpoint);
                vars.TryGetValue("AZURE_OPENAI_API_KEY", out _apiKey);
                vars.TryGetValue("AZURE_OPENAI_API_VERSION", out _apiVersion);
                vars.TryGetValue("AZURE_OPENAI_DEPLOYMENT", out _realtimeDeployment);
                vars.TryGetValue("AZURE_OPENAI_CHAT_DEPLOYMENT", out _chatDeployment);
                vars.TryGetValue("AZURE_OPENAI_VISION_DEPLOYMENT", out _visionDeployment);
                break;
            }
            dir = Path.GetDirectoryName(dir);
        }
    }

    private void SkipIfNoCredentials()
    {
        Assert.False(string.IsNullOrEmpty(_endpoint), "AZURE_OPENAI_ENDPOINT not set in .env");
        Assert.False(string.IsNullOrEmpty(_apiKey), "AZURE_OPENAI_API_KEY not set in .env");
    }

    [Fact]
    public async Task ProbeDeployments_AllExist()
    {
        SkipIfNoCredentials();

        using var http = CreateClient();
        var version = _apiVersion ?? "2024-10-01-preview";
        var baseUrl = $"{_endpoint!.TrimEnd('/')}/openai/deployments";

        var deployments = new Dictionary<string, string?>
        {
            ["Realtime"] = _realtimeDeployment,
            ["Chat"] = _chatDeployment,
            ["Vision"] = _visionDeployment,
        };

        var failures = new List<string>();

        foreach (var (role, name) in deployments)
        {
            if (string.IsNullOrEmpty(name))
            {
                _output.WriteLine($"  {role}: (not configured) - SKIP");
                continue;
            }

            var uri = $"{baseUrl}/{name}/chat/completions?api-version={version}";
            using var content = new StringContent(
                """{"messages":[{"role":"user","content":"test"}],"max_completion_tokens":1}""",
                System.Text.Encoding.UTF8, "application/json");

            var resp = await http.PostAsync(uri, content);
            var found = resp.StatusCode != System.Net.HttpStatusCode.NotFound;

            _output.WriteLine($"  {role}: {name} -> {(int)resp.StatusCode} {resp.ReasonPhrase} -> {(found ? "EXISTS" : "NOT FOUND")}");

            if (!found) failures.Add($"{role}: {name}");
        }

        Assert.True(failures.Count == 0, $"Deployments not found: {string.Join(", ", failures)}");
    }

    [Fact]
    public async Task ProbeDeployment_BadName_Returns404()
    {
        SkipIfNoCredentials();

        using var http = CreateClient();
        var version = _apiVersion ?? "2024-10-01-preview";
        var uri = $"{_endpoint!.TrimEnd('/')}/openai/deployments/DOES-NOT-EXIST-12345/chat/completions?api-version={version}";

        using var content = new StringContent(
            """{"messages":[{"role":"user","content":"test"}],"max_completion_tokens":1}""",
            System.Text.Encoding.UTF8, "application/json");

        var resp = await http.PostAsync(uri, content);
        _output.WriteLine($"Bad deployment probe: {(int)resp.StatusCode} {resp.ReasonPhrase}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task ChatDeployment_AcceptsMinimalRequest()
    {
        SkipIfNoCredentials();
        Assert.False(string.IsNullOrEmpty(_chatDeployment), "AZURE_OPENAI_CHAT_DEPLOYMENT not set in .env");

        using var http = CreateClient();
        var version = _apiVersion ?? "2024-10-01-preview";
        var uri = $"{_endpoint!.TrimEnd('/')}/openai/deployments/{_chatDeployment}/chat/completions?api-version={version}";

        using var content = new StringContent(
            """{"messages":[{"role":"user","content":"Say OK"}],"max_completion_tokens":5}""",
            System.Text.Encoding.UTF8, "application/json");

        _output.WriteLine($"POST {uri}");
        var resp = await http.PostAsync(uri, content);
        var body = await resp.Content.ReadAsStringAsync();
        _output.WriteLine($"Status: {(int)resp.StatusCode} {resp.ReasonPhrase}");
        _output.WriteLine($"Body: {body}");

        Assert.True(resp.IsSuccessStatusCode, $"Chat failed: {(int)resp.StatusCode}: {body}");
    }

    private HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("api-key", _apiKey);
        return http;
    }

    private static Dictionary<string, string> ParseEnv(string path)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;
            var eq = trimmed.IndexOf('=');
            var key = trimmed[..eq].Trim();
            var val = trimmed[(eq + 1)..].Trim();
            if (val.Length > 0) vars[key] = val;
        }
        return vars;
    }
}
