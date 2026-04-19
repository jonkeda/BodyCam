using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using OpenAI.Realtime;

namespace BodyCam.Services;

/// <summary>
/// Subclass of the OpenAI SDK <see cref="RealtimeClient"/> that injects the Azure
/// <c>api-key</c> header into every session start. Uses the GA endpoint
/// (<c>/openai/v1/realtime</c>) — the SDK passes the <c>model</c> query parameter
/// automatically via <see cref="RealtimeClient.StartConversationSessionAsync"/>.
/// </summary>
[Experimental("OPENAI002")]
internal sealed class AzureRealtimeClient : RealtimeClient
{
	private readonly string _apiKey;

	public AzureRealtimeClient(string apiKey, RealtimeClientOptions options)
		: base(new ApiKeyCredential(apiKey), options)
	{
		_apiKey = apiKey;
	}

	public override async Task<RealtimeSessionClient> StartSessionAsync(
		string model,
		string intent,
		RealtimeSessionClientOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		options ??= new();
		options.Headers["api-key"] = _apiKey;
		return await base.StartSessionAsync(model, intent, options, cancellationToken)
			.ConfigureAwait(false);
	}
}
