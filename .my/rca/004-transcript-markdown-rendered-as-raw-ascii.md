# RCA 004: Transcript Markdown Rendered As Raw ASCII

## Status

Fixed in the Phase 2b implementation branch.

## Symptom

AI transcript entries could contain Markdown returned by the model, but the transcript rendered the response through a plain `Label.Text` binding. That made Markdown syntax visible to the user, for example `**bold**`, list markers, headings, code fences, or links, instead of showing formatted transcript text.

## Impact

The Look command can return visually structured answers. When those answers are shown as raw Markdown, the transcript looks like ASCII/plain text output even though the model provided richer Markdown. This is especially noticeable on phone screens because raw syntax adds visual clutter and makes summaries harder to scan.

## Root Cause

`TranscriptEntry.DisplayText` combined the role and message as a single string:

```csharp
$"{Role}: {Text}"
```

`TranscriptView.xaml` then bound that string to `Label.Text`. A plain MAUI label does not parse Markdown, so the app displayed the Markdown source text exactly as received.

## Fix

The transcript model now exposes:

- `FormattedText`, a MAUI `FormattedString` rendered from Markdown.
- Markdown-aware `AccessibleText`, so screen reader text is plain text rather than Markdown source.

The view now binds the transcript label to `FormattedText`. The formatter uses Markdig to parse Markdown into an AST and maps the supported pieces to MAUI spans:

- bold and italic emphasis
- inline code and code blocks
- headings
- ordered and unordered lists
- block quotes
- links as underlined text

This keeps transcript rendering native to MAUI and avoids a WebView for chat text.

## Dependency Decision

Markdig was added to `src/BodyCam/BodyCam.csproj` rather than hand-parsing Markdown. That is safer because Markdown is not a regular string-cleanup problem; it has nested inline structure, escaped characters, lists, links, and code spans. Using a parser lets us support the common cases now and extend rendering later without fragile replacements.

## Validation

Added unit coverage for:

- `FormattedText` notification when transcript text changes.
- bold, italic, and inline code rendering without raw Markdown syntax.
- Markdown list rendering.
- accessibility text conversion from Markdown to plain text.

## Prevention

When AI output can contain Markdown, UI surfaces should bind to a formatted rendering model rather than raw text. Future transcript features should add renderer tests when adding support for more Markdown constructs such as tables, images, or task lists.
