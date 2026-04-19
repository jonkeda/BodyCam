using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using OpenAI.Realtime;

namespace BodyCam.Services;

/// <summary>
/// Subclass of the OpenAI SDK <see cref="RealtimeClient"/> that injects Azure-specific
/// query parameters (<c>api-version</c>, <c>deployment</c>) and the <c>api-key</c> header
/// into every session start. Required because the base SDK strips query parameters from
/// the endpoint URL during WebSocket URI construction.
/// </summary>
[Experimental("OPENAI002")]
internal sealed class AzureRealtimeClient : RealtimeClient
{
	private readonly string _deployment;
	private readonly string _apiVersion;
	private readonly string _apiKey;

	public AzureRealtimeClient(
		string apiKey,
		RealtimeClientOptions options,
		string deployment,
		string apiVersion)
		: base(new ApiKeyCredential(apiKey), options)
	{
		_apiKey = apiKey;
		_deployment = deployment;
		_apiVersion = apiVersion;
	}

	public override async Task<RealtimeSessionClient> StartSessionAsync(
		string model,
		string intent,
		RealtimeSessionClientOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		options ??= new();
		options.QueryString = $"api-version={Uri.EscapeDataString(_apiVersion)}&deployment={Uri.EscapeDataString(_deployment)}";
		options.Headers["api-key"] = _apiKey;
		return await base.StartSessionAsync(model, intent, options, cancellationToken)
			.ConfigureAwait(false);
	}
}
