# Azure OpenAI — Budget Setup Guide

How to configure BodyCam with the cheapest Azure OpenAI models.

---

## Overview

Azure OpenAI uses **deployments** — you deploy a specific model under a name you choose. BodyCam needs four deployments, one per role. This guide uses the cheapest model for each.

## Budget Model Picks

| Role | Model to Deploy | Why |
|------|----------------|-----|
| Voice (Realtime) | `gpt-realtime-mini` | ~50x cheaper audio than `gpt-realtime-1.5` |
| Chat | `gpt-5.4-nano` | Cheapest text model ($0.20/$1.25 per 1M tokens) |
| Vision | `gpt-5.4-mini` | Cheapest with vision support ($0.75/$4.50) |
| Transcription | `whisper-1` | Standard Azure transcription model |

**Estimated cost:** ~$5–10/hr of casual use (vs ~$115/hr with premium models).

## Step 1 — Create an Azure OpenAI Resource

1. Go to [Azure Portal](https://portal.azure.com) → **Create a resource** → search **Azure OpenAI**
2. Select a region that supports all four models. **East US 2** or **Sweden Central** have the best coverage.
3. Choose **Standard (S0)** pricing tier
4. Once created, grab your **API key** from **Keys and Endpoint**

## Step 2 — Create Deployments

In the [Azure AI Foundry portal](https://ai.azure.com) (or Azure Portal → your OpenAI resource → **Model deployments**):

| Deployment Name | Model | Version | Type |
|----------------|-------|---------|------|
| `bodycam-realtime` | gpt-realtime-mini | 2025-12-15 | Global Standard |
| `bodycam-chat` | gpt-5.4-nano | 2026-03-17 | Global Standard |
| `bodycam-vision` | gpt-5.4-mini | 2026-03-17 | Global Standard |
| `bodycam-transcribe` | whisper-1 | — | Global Standard |

> You can name deployments whatever you like. The names above are suggestions.

## Step 3 — Configure BodyCam

### Option A: Settings Page (recommended)

1. Open the app → **Settings** tab
2. Set **Provider** to `azure`
3. Fill in:
   - **Resource Name** — your Azure resource name (e.g., `my-openai-eastus2`)
   - **API Version** — `2024-10-01` (GA)
   - **Realtime Deployment** — `bodycam-realtime`
   - **Chat Deployment** — `bodycam-chat`
   - **Vision Deployment** — `bodycam-vision`
   - **Transcription Deployment** — `bodycam-transcribe`
4. Tap **Change** under API Key and enter your Azure key

### Option B: `.env` file (development)

```env
OPENAI_PROVIDER=azure
AZURE_OPENAI_API_KEY=your-azure-key-here
AZURE_OPENAI_RESOURCE=my-openai-eastus2
AZURE_OPENAI_DEPLOYMENT=bodycam-realtime
AZURE_OPENAI_CHAT_DEPLOYMENT=bodycam-chat
AZURE_OPENAI_VISION_DEPLOYMENT=bodycam-vision
AZURE_OPENAI_TRANSCRIPTION_DEPLOYMENT=bodycam-transcribe
AZURE_OPENAI_API_VERSION=2024-10-01
```

## Region Availability

Not all models are in all regions. Pick one of these for full coverage:

| Region | Realtime Mini | GPT-5.4 Nano | GPT-5.4 Mini | Transcribe |
|--------|:---:|:---:|:---:|:---:|
| **East US 2** | ✅ | ✅ | ✅ | ✅ |
| **Sweden Central** | ✅ | ✅ | ✅ | ✅ |
| **South Central US** | — | ✅ | ✅ | ✅ |
| **Poland Central** | — | ✅ | ✅ | ✅ |

> If Realtime Mini isn't available in your region, use `gpt-realtime-1.5` instead (more expensive but wider availability).

## Upgrading Later

To switch individual models without changing your whole setup, just create a new deployment with the better model and update the deployment name in Settings. No code changes needed.

| Role | Budget → Premium |
|------|-----------------|
| Voice | `gpt-realtime-mini` → `gpt-realtime-1.5` |
| Chat | `gpt-5.4-nano` → `gpt-5.4-mini` or `gpt-5.4` |
| Vision | `gpt-5.4-mini` → `gpt-5.4` |
| Transcription | `whisper-1` → `gpt-4o-transcribe` |
