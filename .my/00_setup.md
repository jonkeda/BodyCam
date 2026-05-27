# Wireframes Setup

## Why Wireframes (not Figma/Penpot)

Figma Starter plan: 3 pages, 1 variable mode, **6 MCP calls/month** — exhausted.
Penpot: requires browser plugin bridge, complex setup.
Wireframes: text-based `.wire` files in-repo, VS Code extension with live preview, 50+ controls, SVG/PNG export.

## Source

https://github.com/jonkeda/Wireframes

## VS Code Extension

Search for **"Wireframe"** in Extensions (`Ctrl+Shift+X`) and install.

### Features

- Syntax highlighting
- Live preview (`Ctrl+Shift+V`)
- IntelliSense autocomplete
- Error diagnostics
- Export to SVG/PNG
- 4 themes: Sketch, Blueprint, Clean, Realistic
- 50+ controls, 6 layouts

## Usage

1. Create a `.wire` file (e.g. `.my/wireframes/main-page.wire`)
2. Write wireframe syntax:
   ```
   wireframe clean
       Button "Hello World" primary
   /wireframe
   ```
3. Press `Ctrl+Shift+V` to open preview

## npm Packages (programmatic use)

```
@jonkeda/wireframe-core    — Parser and renderer
@jonkeda/wireframe-themes  — Additional themes
@jonkeda/wireframe-mermaid — Mermaid.js plugin
@jonkeda/wireframe-cli     — Command-line tool
```
```

The MCP server exposes tools for querying/modifying Penpot designs.
The plugin runs inside Penpot's browser context and executes operations via the Plugin API.
The LLM can write and execute arbitrary Plugin API code through the MCP bridge.
