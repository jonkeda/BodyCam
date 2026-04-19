using Microsoft.Extensions.AI;

namespace BodyCam.Services;

/// <summary>
/// The app's <see cref="IChatClient"/> implementation.
/// Resolves the API key asynchronously on first use — no blocking, no Lazy&lt;T&gt;.
/// If the key is missing, calls fail with a clear error instead of crashing at startup.
/// </summary>
public sealed class AppChatClient : IChatClient
{
	private readonly IApiKeyService _apiKeyService;
	private readonly AppSettings _settings;
	private IChatClient? _inner;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public AppChatClient(IApiKeyService apiKeyService, AppSettings settings)
	{
		_apiKeyService = apiKeyService;
		_settings = settings;
	}

	public async Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> chatMessages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		var client = await GetOrCreateClientAsync();
		return await client.GetResponseAsync(chatMessages, options, cancellationToken);
	}

	public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> chatMessages,
		ChatOptions? options = null,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var client = await GetOrCreateClientAsync();
		await foreach (var update in client.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
		{
			yield return update;
		}
	}

	public object? GetService(Type serviceType, object? serviceKey = null)
		=> _inner?.GetService(serviceType, serviceKey);

	public void Dispose()
	{
		_inner?.Dispose();
		_gate.Dispose();
	}

	private async Task<IChatClient> GetOrCreateClientAsync()
	{
		if (_inner is not null)
			return _inner;

		await _gate.WaitAsync();
		try
		{
			if (_inner is not null)
				return _inner;

			var key = await _apiKeyService.GetApiKeyAsync();
			if (string.IsNullOrEmpty(key))
				throw new InvalidOperationException(
					"API key not configured. Open Settings and enter your OpenAI or Azure OpenAI key.");

			if (_settings.Provider == OpenAiProvider.Azure)
			{
				var credential = new Azure.AzureKeyCredential(key);
				var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(
					new Uri(_settings.AzureEndpoint!), credential);
				_inner = azureClient.GetChatClient(_settings.AzureChatDeploymentName!).AsIChatClient();
			}
			else
			{
				var openAiClient = new OpenAI.OpenAIClient(key);
				_inner = openAiClient.GetChatClient(_settings.ChatModel).AsIChatClient();
			}

			return _inner;
		}
		finally
		{
			_gate.Release();
		}
	}
}
