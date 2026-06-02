# Phase 3 - Grok Text, Tools, And Vision

## Goal

Make Grok usable for text, tool calls, structured outputs, and image
understanding.

## Work

- Add `GrokTextProvider` using xAI's OpenAI-compatible API surface where
  possible.
- Add `GrokVisionProvider` for image inputs.
- Map BodyCam tool definitions to xAI/Grok function calling.
- Preserve current command prompts and command settings from M44.
- Support model options for:
  - general chat/text;
  - vision-capable Grok models;
  - cheaper non-reasoning or fast models when appropriate.
- Add response normalization so the transcript and command layer do not care
  which provider generated the answer.

## Acceptance

- A text prompt returns a response through Grok.
- Look can capture an image and get a Grok vision answer.
- Read can send an image and get text extraction/summarization.
- Tool calls can be received, dispatched, and answered.
- Markdown transcript rendering still works for Grok output.

## Risks

- Model names and availability change quickly. Use provider model metadata and
  connection tests instead of hard-coded assumptions scattered through the app.
- Image input history may need provider-specific handling. xAI docs advise not
  storing image request/response history server-side for image understanding
  requests.
