# Phase 5 - Grok Images And Command Capabilities

## Goal

Use Grok's image capabilities where they help BodyCam, without mixing
generative images into safety-critical camera descriptions.

## Work

- Add `GrokImageGenerationProvider` for text-to-image and image editing.
- Keep image generation separate from Look/Read/Scan.
- Add provider capability checks so commands know whether the selected
  provider supports:
  - vision input;
  - image generation;
  - image editing;
  - structured visual output.
- Add future command candidates that may use image generation, such as:
  - create a visual instruction card;
  - generate a map-like simplified scene diagram;
  - edit/crop/annotate a captured image for sharing.

## Acceptance

- Image generation can be tested from a debug or future command surface.
- Look and Read continue to use real captured images and never substitute a
  generated image as evidence.
- The app can explain when the active provider supports vision but not image
  generation, or image generation but not realtime voice.

## Out Of Scope

- Making generated images part of normal blind-navigation assistance.
- Saving generated images to a gallery without an explicit user action.
