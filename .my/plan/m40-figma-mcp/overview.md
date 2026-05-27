# M40 — Figma MCP Server Setup

**Status:** Ready to execute
**Goal:** Install and configure the Figma MCP server so AI coding agents in
VS Code can read Figma design context, generate code from frames, and extract
variables/components directly from Figma files.

---

## Background

Figma provides an official remote MCP server at `https://mcp.figma.com/mcp`
(Streamable HTTP). It requires no local install — the server is hosted by
Figma. Authentication happens via OAuth in the browser on first use.

There is also a popular community alternative, **Framelink MCP for Figma**
(`figma-developer-mcp` on npm), which runs locally via `npx` and uses a
Figma Personal Access Token. It simplifies API responses to reduce context
size, which can improve accuracy.

This plan covers both options.

---

## Option A — Official Figma MCP Server (recommended)

This is Figma's hosted server. No npm install, no token management.

### VS Code Setup

1. Open Command Palette: `Ctrl+Shift+P` → `MCP: Add Server`.
2. Select **HTTP**.
3. Paste the server URL: `https://mcp.figma.com/mcp` and press Enter.
4. When prompted for a server ID, enter `figma`.
5. Choose whether to add globally or workspace-only.

This produces the following in your `mcp.json`:

```json
{
  "servers": {
    "figma": {
      "type": "http",
      "url": "https://mcp.figma.com/mcp"
    }
  }
}
```

6. Open Agent mode (`Ctrl+Alt+I`) and type `#get_design_context` to verify
   the tools are available. If nothing shows, restart VS Code.
7. On first tool call, a browser window opens for Figma OAuth. Sign in and
   authorize.

### Features

- **Write to canvas** — create/modify native Figma content from the agent (beta, free during beta)
- **Generate code from selected frames** — paste a Figma frame link into chat
- **Extract design context** — variables, components, layout data
- **Code Connect** — reuse actual codebase components for consistency
- **Generate Figma designs from web pages** (rolling out)

### Rate Limits

| Plan | Limit |
|---|---|
| Starter / View / Collab seats | 6 tool calls per month |
| Dev or Full seat (Professional+) | Per-minute, same as Figma REST API Tier 1 |

---

## Option B — Framelink MCP for Figma (community, local)

Runs locally via `npx`. Requires a Figma Personal Access Token.

### 1. Create a Figma Personal Access Token

1. Go to Figma → Settings → Personal Access Tokens.
2. Create a new token (see [Figma docs](https://help.figma.com/hc/en-us/articles/8085703771159-Manage-personal-access-tokens)).
3. Copy the token value.

### 2. Configure in VS Code

Add to your `mcp.json` (or `.vscode/mcp.json` for workspace scope):

```json
{
  "servers": {
    "figma-framelink": {
      "command": "cmd",
      "args": ["/c", "npx", "-y", "figma-developer-mcp", "--figma-api-key=YOUR-KEY", "--stdio"]
    }
  }
}
```

Replace `YOUR-KEY` with the Personal Access Token.

Alternatively, use an environment variable to avoid committing the key:

```json
{
  "servers": {
    "figma-framelink": {
      "command": "cmd",
      "args": ["/c", "npx", "-y", "figma-developer-mcp", "--stdio"],
      "env": {
        "FIGMA_API_KEY": "${input:figmaApiKey}"
      }
    }
  }
}
```

### 3. Verify

Open Agent mode, paste a Figma frame URL, and ask the agent to implement
the design. The server will fetch and simplify the Figma layout data.

---

## Usage Tips

1. **Paste a Figma frame link** into chat and ask the agent to implement it.
   The agent extracts the node ID from the URL automatically.
2. **Break large selections into smaller parts** — individual components or
   logical sections. Large selections can slow tools down or produce
   incomplete results.
3. **Name Figma layers semantically** (e.g. `CardContainer`, not `Group 5`).
4. **Use Auto Layout** in Figma to communicate responsive intent.
5. **Use `get_design_context`** for structured layout info and
   `get_variable_defs` to extract design tokens (color, spacing, typography).
6. **Add project rules** (e.g. in `.github/copilot-instructions.md`) to guide
   framework translation — the MCP returns React + Tailwind representations
   by default, which the agent should translate to your actual stack.

---

## References

- [Figma MCP Server Guide (official)](https://github.com/figma/mcp-server-guide)
- [Framelink MCP for Figma (community)](https://github.com/GLips/Figma-Context-MCP)
- [Figma Developer Docs — MCP](https://developers.figma.com/docs/figma-mcp-server/)
- [VS Code MCP docs](https://code.visualstudio.com/docs/copilot/chat/mcp-servers)
