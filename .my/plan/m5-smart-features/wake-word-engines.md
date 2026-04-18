# Wake Word Engine Comparison

Which engine should BodyCam use for always-on wake word detection?
This document compares all viable options — commercial and open-source.

---

## Summary

| Engine | License | AccessKey? | .NET SDK | Custom Keywords | On-device | Maintained |
|--------|---------|------------|----------|-----------------|-----------|------------|
| **Porcupine** | Apache-2.0 (code) | ✅ Required | ✅ NuGet `Porcupine` | `.ppn` via Console | ✅ | ✅ Active (v4.0, Dec 2025) |
| **openWakeWord** | Apache-2.0 (code), CC BY-NC-SA 4.0 (models) | ❌ None | ❌ Python only | ✅ Train via Colab | ✅ | ✅ Active (v0.6.0, Feb 2024) |
| **Mycroft Precise** | Apache-2.0 | ❌ None | ❌ Python only | ✅ Train yourself | ✅ | ❌ Dead (last commit 2019) |
| **WeKws** | Apache-2.0 | ❌ None | ❌ Python/C++ | ✅ Train yourself | ✅ | ⚠️ Low activity |
| **Snowboy** | Apache-2.0 | ❌ None | ❌ C/Python | ✅ Via web | ✅ | ❌ Dead (discontinued) |

---

## 1. Porcupine (Picovoice)

**Repo:** [github.com/Picovoice/porcupine](https://github.com/Picovoice/porcupine)  
**Stars:** 4.8k | **License:** Apache-2.0 | **Latest:** v4.0 (Dec 2025)

### What It Is
Commercial-grade wake word engine using deep neural networks. Cross-platform
(Windows, Android, iOS, Linux, RPi, MCU, Web). Has a first-class .NET NuGet
package (`Porcupine`).

### AccessKey Requirement
**Requires a Picovoice AccessKey** from [console.picovoice.ai](https://console.picovoice.ai).
The key is a license/activation token — NOT a network API key. Porcupine runs
100% on-device; the key just gates SDK initialization.

**Free tier:** Unlimited recognition, up to 3 devices. No cost.  
**Paid tier:** Removes device limits. Enterprise pricing.

### Custom Keywords
Generate `.ppn` model files via Picovoice Console (web UI). Select language,
type your phrase, download the model. Takes seconds.

### .NET API
```csharp
using Pv;

var handle = Porcupine.FromKeywordPaths(
    accessKey,
    new List<string> { "path/to/keyword.ppn" });

while (true)
{
    int index = handle.Process(GetNextAudioFrame());
    if (index >= 0)
    {
        // Wake word detected!
    }
}
```

### Pros
- First-class .NET/C# support (NuGet package)
- Extremely low latency (<50ms) and power (~10mW)
- Cross-platform: Windows, Android, iOS out of the box
- Easy custom keyword creation (web UI, no ML knowledge needed)
- GPU/multi-core support in v4.0
- Battle-tested, 4.8k GitHub stars
- 9 languages supported

### Cons
- **Requires AccessKey** (free tier is limited to 3 devices)
- `.ppn` files are platform-specific (need separate files per OS)
- Proprietary model format — can't inspect or modify models
- Custom keywords require Picovoice Console account

### BodyCam Fit: ⭐⭐⭐⭐⭐
Perfect fit. Native .NET, cross-platform, trivial to integrate with existing
`IWakeWordService` interface. Free tier is fine for development and personal use.

---

## 2. openWakeWord

**Repo:** [github.com/dscripka/openWakeWord](https://github.com/dscripka/openWakeWord)  
**Stars:** 2.1k | **License:** Apache-2.0 (code), CC BY-NC-SA 4.0 (pre-trained models) | **Latest:** v0.6.0 (Feb 2024)

### What It Is
Fully open-source wake word framework. Uses Google's pre-trained speech embedding
model + small classification heads. Processes 80ms audio frames, returns 0–1
confidence scores. Models are ONNX format.

**No AccessKey. No account. No license restrictions on code.**

### Custom Keywords
Train custom models via Google Colab notebook in <1 hour using 100% synthetic
speech (text-to-speech). No real audio data collection needed.

### The Catch: Python Only
**No .NET SDK.** Python-only library. To use from C#/.NET, you would need:

1. **ONNX Runtime interop** — Load the `.onnx` models directly via
   `Microsoft.ML.OnnxRuntime` NuGet package. You'd need to reimplement the
   melspectrogram preprocessing, embedding extraction, and classification
   pipeline in C#.
2. **Python subprocess** — Run openWakeWord in a Python process, communicate
   via named pipes or gRPC. Adds latency and deployment complexity.
3. **C++ port** — A basic [C++ implementation](https://github.com/rhasspy/openWakeWord-cpp)
   exists (by @synesthesiam). Could P/Invoke into it, but it's minimal.

### Pre-trained Models
Includes models for: "alexa", "hey mycroft", "hey jarvis", "hey rhasspy",
weather commands, timer commands. English only.

### Performance
- Competitive with Porcupine on accuracy (see benchmark charts in repo)
- Can run 15-20 models simultaneously on a single RPi 3 core
- **Larger than Porcupine** — not suitable for microcontrollers
- 80ms frame processing (vs Porcupine's per-frame)

### Pros
- **Completely free** — no keys, no accounts, no device limits
- Open-source models (can inspect, modify, retrain)
- Train custom words with zero real data (synthetic speech only)
- Good accuracy, competitive with commercial options
- VAD (voice activity detection) built in
- Speex noise suppression support

### Cons
- **No .NET SDK** — significant integration work required
- Python-only ecosystem
- English only
- Pre-trained models are CC BY-NC-SA 4.0 (non-commercial!)
- Larger models than Porcupine (not suitable for MCU/embedded)
- Slower development pace than Porcupine

### BodyCam Fit: ⭐⭐⭐
Strong engine, but the lack of .NET SDK is a major obstacle. Would require
either reimplementing the pipeline in C# via ONNX Runtime or running a Python
sidecar process. The non-commercial license on pre-trained models is also
problematic if BodyCam is ever distributed.

---

## 3. Mycroft Precise

**Repo:** [github.com/MycroftAI/mycroft-precise](https://github.com/MycroftAI/mycroft-precise)  
**Stars:** 963 | **License:** Apache-2.0 | **Latest:** v0.3.0 (Apr 2019)

### What It Is
Lightweight RNN-based (GRU) wake word listener from Mycroft AI. Simple
architecture — a single recurrent network that monitors audio streams.

### ⚠️ Dead Project
**Last commit: 2019.** Mycroft AI shut down in 2023. No maintenance, no updates,
no community. Python-only, Linux-only.

### Pros
- Fully open source, Apache-2.0
- Simple architecture (single GRU)
- Community-trained models available

### Cons
- **Dead project** — no updates since 2019
- Python and Linux only
- No .NET SDK
- Requires TensorFlow (heavy dependency)
- Outdated dependencies, likely broken on modern Python

### BodyCam Fit: ⭐
Not viable. Dead project, Python/Linux only, no .NET support.

---

## 4. WeKws (Wenet Keyword Spotting)

**Repo:** [github.com/wenet-e2e/wekws](https://github.com/wenet-e2e/wekws)  
**Stars:** 709 | **License:** Apache-2.0 | **Latest:** commits ~7 months ago

### What It Is
Production-oriented keyword spotting toolkit focused on IoT/edge devices.
Supports multiple architectures (DS-CNN, MDTC, etc.) for small-footprint models.
Has ONNX Runtime C++ inference support.

### Pros
- Apache-2.0 (fully permissive)
- Designed for low-power IoT
- C++ runtime available (could P/Invoke)
- Multiple model architectures
- Chinese language support

### Cons
- No .NET SDK
- Training requires PyTorch + dataset preparation
- Primarily research-oriented
- Low community activity
- No pre-built custom keyword pipeline

### BodyCam Fit: ⭐⭐
Interesting for edge deployment but too research-oriented. No .NET integration
path without significant effort.

---

## 5. ONNX Runtime DIY Approach

Instead of using a pre-built library, you could:

1. Train a model using openWakeWord's pipeline (Python, one-time)
2. Export to `.onnx` format
3. Load in C# via `Microsoft.ML.OnnxRuntime` NuGet
4. Implement the audio preprocessing (melspectrogram) in C#
5. Run inference directly

### Pros
- Completely free, no keys
- Native .NET via ONNX Runtime NuGet
- Full control over models
- Can use openWakeWord's training pipeline

### Cons
- Must reimplement melspectrogram + embedding pipeline in C# (~500-800 lines)
- Must maintain the preprocessing code yourself
- Need to validate accuracy matches Python reference
- Training still requires Python
- Significant upfront engineering investment

### BodyCam Fit: ⭐⭐⭐
Viable but high effort. Would give full independence from any vendor, at the
cost of reimplementing audio preprocessing in C#.

---

## Recommendation

### For BodyCam: Use Porcupine

| Factor | Porcupine | Best Free Alternative |
|--------|-----------|----------------------|
| .NET SDK | ✅ NuGet, 5 lines of code | ❌ Reimplement in C# |
| Time to integrate | Hours | Weeks |
| Cross-platform | ✅ Windows + Android + iOS | ⚠️ Manual per platform |
| Custom keywords | Web UI, instant | Train model, ~1 hour |
| Cost | Free (3 devices) | Free |
| Accuracy | Excellent | Competitive |
| Power consumption | ~10mW | Higher |
| Maintenance burden | Zero (NuGet updates) | High (own code) |

**The free tier (3 devices) is sufficient for BodyCam's use case** — personal
smart glasses don't need thousands of device activations. The AccessKey is free,
requires no payment info, and Porcupine runs fully on-device (no data sent anywhere).

### If Free/No-Key Is Hard Requirement

Go with the **ONNX Runtime DIY approach** using openWakeWord's training pipeline:
1. Train models in Python via openWakeWord
2. Export to ONNX
3. Create `OnnxWakeWordService : IWakeWordService` using `Microsoft.ML.OnnxRuntime`
4. Reimplement melspectrogram preprocessing in C#

This gives zero vendor dependency at the cost of ~1-2 weeks of engineering work
for the preprocessing pipeline.

### Hybrid Approach

Use Porcupine for development and initial releases (fast integration), but
design `IWakeWordService` to be engine-agnostic (already done!). If the AccessKey
becomes a problem later, swap in the ONNX approach behind the same interface.
This is essentially what the current architecture already supports.
