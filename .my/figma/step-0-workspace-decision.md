# Step 0 - Workspace Decision

**Status:** Waiting for user validation  
**Date:** 2026-05-21

---

## Decision

Use a clean Figma file named **BodyCam UX System** as the maintained design source for BodyCam UX work.

This should be a clean Figma file, not an import dump. The current MAUI app remains the implementation source while we build the first Figma baseline from XAML inventory and screenshots.

Preferred creation path:

1. Use the official Figma MCP `create_new_file` tool if it is available in the active MCP client.
2. If MCP file creation is not exposed in the current client, create the file manually in Figma.
3. If MCP creates the file in Drafts, move it into the BodyCam project afterward if desired.

---

## Repo-Side Checks

Completed:

- `.vscode/mcp.json` is valid JSON.
- The only configured MCP server is the official hosted Figma server.
- No `figma-framelink`, `figma-developer-mcp`, or `FIGMA_API_KEY` setup is required.

Current MCP config:

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

---

## How To Expose The Figma MCP Tools In VS Code

The `.vscode/mcp.json` file registers the server for VS Code, but the tools become available only after the MCP server is started and authorized in the active client.

Steps:

1. Open `.vscode/mcp.json` in VS Code.
2. Use the inline **Start** action shown above the `figma` server, or open the Command Palette and run:

   ```text
   MCP: List Servers
   ```

3. Select the `figma` server and start it.
4. Complete the Figma OAuth flow in the browser.
5. Open Copilot/agent chat in Agent mode.
6. Ask for a Figma MCP action, for example:

   ```text
   Use Figma MCP create_new_file to create a new design file named "BodyCam UX System".
   ```

If the server is running but tools do not appear, try:

- `MCP: List Servers` -> `figma` -> restart.
- `Developer: Reload Window`.
- Confirm the workspace is trusted.
- Confirm the Figma account has the required permission/seat for write-to-canvas actions.

Important: this repository config exposes Figma MCP to clients that read VS Code MCP configuration. It does not automatically expose Figma tools to every separate AI runtime. In this Codex session, no `mcp__figma__...` tools are currently present in the active tool list, so file creation has to be done from a VS Code/Figma MCP-enabled agent or manually.

---

## User Validation Needed

Please create or confirm the Figma file:

```text
BodyCam UX System
```

Preferred MCP prompt, if your client exposes the Figma `create_new_file` tool:

```text
Use Figma MCP create_new_file to create a new design file named "BodyCam UX System".
```

Recommended final location:

```text
Figma workspace/project: BodyCam
File: BodyCam UX System
```

For Step 0, the file can stay empty. We will create pages and structure in Step 2.

---

## Acceptance Checklist

Step 0 is accepted when:

- The Figma file `BodyCam UX System` exists.
- You can open it in Figma.
- VS Code can see the official `figma` MCP server.
- No local Figma token setup is needed.

After acceptance, move to Step 1: current XAML inventory.
