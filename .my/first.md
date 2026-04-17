You are my coding assistant. I am building a .NET MAUI app that uses the Microsoft Agentic Framework (MAF) together with OpenAI’s real-time streaming voice API.

Generate a complete starter template with the following components:

PROJECT STRUCTURE
-----------------
Create a .NET MAUI solution with these folders:

- /Agents
    - VoiceInputAgent.cs
    - ConversationAgent.cs
    - VoiceOutputAgent.cs
    - VisionAgent.cs (stub, optional)
- /Services
    - AudioInputService.cs
    - AudioOutputService.cs
    - CameraService.cs (stub)
    - OpenAiStreamingClient.cs
- /Orchestration
    - AgentOrchestrator.cs
- /Models
    - SessionContext.cs
- /Pages
    - MainPage.xaml
    - MainPage.xaml.cs

GOAL
----
The app should:
1. Capture microphone audio in small chunks (20–100 ms)
2. Stream those chunks to OpenAI’s real-time API
3. Receive partial transcripts from OpenAI
4. Pass transcripts into a ConversationAgent (MAF)
5. Stream TTS audio back from OpenAI
6. Play audio frames immediately (low latency)
7. (Optional) Accept camera frames for vision

AGENTS
------
VoiceInputAgent:
- Receives audio chunks from AudioInputService
- Sends them to OpenAiStreamingClient
- Emits partial transcripts into the AgentContext

ConversationAgent:
- Receives text from VoiceInputAgent
- Uses OpenAI (gpt-5.4 or gpt-5.4-mini) for reasoning
- Produces a reply string

VoiceOutputAgent:
- Sends reply text to OpenAiStreamingClient
- Receives streaming audio frames
- Sends frames to AudioOutputService for playback

VisionAgent (stub):
- Accepts byte[] frames
- Sends them to OpenAI vision endpoint
- Returns a description string

ORCHESTRATOR
------------
AgentOrchestrator:
- Runs VoiceInputAgent continuously
- When transcripts arrive, triggers ConversationAgent
- Sends output to VoiceOutputAgent
- Maintains a SessionContext object

SERVICES
--------
AudioInputService:
- Uses platform microphone
- Streams PCM audio chunks (16 kHz or 24 kHz)

AudioOutputService:
- Plays streaming audio frames as they arrive

OpenAiStreamingClient:
- Wraps OpenAI real-time streaming API
- Supports:
    - SendAudioAsync(chunk)
    - ReceiveTranscriptsAsync()
    - SynthesizeStreamingAsync(text)

CameraService (stub):
- Provides IAsyncEnumerable<byte[]> for frames

MAIN PAGE
---------
MainPage.xaml:
- A simple UI with:
    - Start/Stop button
    - Transcript display
    - Debug console

MainPage.xaml.cs:
- Starts/stops the orchestrator
- Binds transcript updates to UI

CODING STYLE
------------
- Use async/await everywhere
- Use dependency injection
- Use interfaces for services
- Keep code clean and modular
- Add comments explaining each part

OUTPUT
------
Generate:
1. All class files with namespaces
2. Minimal working implementations
3. TODO comments where needed
4. A runnable MAUI project skeleton

Do NOT include API keys. Use placeholders like "YOUR_OPENAI_KEY". Create a setting class

Begin generating the full project now.
