# Thirty-Dollar Glasses

*Building an AI assistant from cheap sunglasses and stubbornness.*

---

The RayBan Metas cost $399 and you can't change a thing about them. The TKYUAN glasses from Alibaba cost $30, had a 1080p camera, Bluetooth speakers, and a mic. I ordered two pairs — first hardware always breaks — and opened VS Code.

The idea was simple: the glasses are dumb sensors. The phone is the brain. A .NET MAUI app captures audio from the glasses mic, streams it to OpenAI's Realtime API over WebSocket, pipes the camera through GPT-4 Vision, and pushes speech back through the open-ear speakers. Total hardware cost: under $30.

Two days later, it worked.

---

Day one was audio and conversation. PCM at 24kHz, 16-bit mono, streamed in 100ms chunks. The first time the AI spoke back through my laptop speakers, the latency was under half a second. Then came the bugs — overlapping audio playback, garbled transcripts from out-of-order deltas, black camera frames from autofocus lag. Seven root cause analyses, seven fixes.

By 1 PM I was having a conversation with it. I told it a joke. It laughed — or generated something close enough.

Then I pointed the webcam at my desk and said "What do you see?"

*"A white desk with a 27-inch monitor showing a code editor. A mechanical keyboard, a wireless mouse, and a coffee mug with steam rising from it."*

It saw the steam.

---

Day two was the leap. One architectural decision changed everything: **every feature is a tool**. No switch statements, no hardcoded capabilities. Each tool — reading text, finding objects, saving memories, making phone calls — is a single class implementing one interface. The LLM decides when to call what.

Thirteen tools. 229 unit tests. Thirteen live API tests that sent real prompts and verified the model picked the right function. "Read the text on the sign" → `read_text`. "Remember my car is in spot B7" → `save_memory`. "Find my red coffee mug" → `find_object`. All thirteen passed.

One surprise: asked to navigate to "the nearest Starbucks," the model called `lookup_address` first, then `navigate_to`. Smarter than expected.

---

You put on a pair of sunglasses. You say "Hey BodyCam." The phone in your pocket wakes up.

"What do you see?"

*"You're at a crosswalk. The sign says Broadway. There's a coffee shop to your left."*

"Read the menu."

*"Espresso $3.50, Latte $4.75, Cold Brew $5.00..."*

"Remember — the cold brew is five dollars."

*"Got it."*

Nobody around you knows. It looks like you're wearing sunglasses.

Thirty dollars. Two days. Open source. Yours.
