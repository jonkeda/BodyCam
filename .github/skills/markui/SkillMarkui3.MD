---
name: markui3
description: 'Generate current MarkUI ASCII wireframes and maintain MarkUI examples. Use when creating UI mockups, screen layouts, wireframes, prototypes, .markui files, or markui fenced code blocks; also use when updating MarkUI examples after parser or renderer changes.'
argument-hint: 'Describe the screen, component, flow, or example to wireframe'
---
# MarkUI Wireframe Generation v3

Generate UI wireframes using the current MarkUI language. Favor source that is readable as ASCII and also renders cleanly in the VS Code preview.

## Core Rules

1. `.markui` files contain one screen and no prose.
2. Markdown files can contain many screens using ` ```markui ` or named ` ```markui:component-name ` fences.
3. Visible wireframes contain UI only. Do not render requirements, business rules, implementation notes, or prompt instructions as UI text.
4. Text inside inputs is example data, not placeholder copy.
5. Widgets on the same line imply horizontal layout. Widgets on separate lines imply vertical layout.
6. Prefer ASCII. Unicode box drawing is accepted, but generated examples should use `+`, `-`, `|`, `[x]`, `#1`, and similar keyboard-friendly syntax.
7. Keep examples visually conservative: valid boxes, enough spacing, no overlapping content, and no decorative border characters outside the supported grammar.

## File Format

- Use `.markui` for a single screen or component.
- Use `.md` with fenced blocks for docs, flows, and component libraries.
- Use named blocks to define reusable components:

````
```markui:user-card
+--- User ----------+
| !==IMG==!         |
| <Jane Doe______>  |
+-------------------+
```
````

- Reference named blocks or component files with `@name` at line start.
- Define icon meanings (`#1`, `#2`) in surrounding Markdown, not inside the wireframe.

## Bracket System

| Syntax    | Meaning                                                    | Examples                                     |
| --------- | ---------------------------------------------------------- | -------------------------------------------- |
| `[ ]`   | Buttons, checks, slider/progress, rating, stepper, spinner | `[Save]`, `[x]`, `[====....]`          |
| `[[ ]]` | Active tab or active page                                  | `[[Board]]`, `[[3]]`                     |
| `< >`   | Inputs and dropdowns                                       | `<Jane Doe____>`, `<English v>`          |
| `{ / }` | Toggle                                                     | `{[on]/off}`, `{on/[off]}`               |
| `{n}`   | Badge                                                      | `{3}`, `{!}`                             |
| `( )`   | Radio or chip                                              | `(*)`, `( )`, `(React)`, `(React x)` |
| `#N`    | Numbered icon                                              | `#1`, `#2`                               |
| `!`     | Image placeholder                                          | `!==IMG==!`                                |

## Widget Rules

### Square Brackets

Content inside `[]` is interpreted in this order:

| Content                                 | Widget             | Example          |
| --------------------------------------- | ------------------ | ---------------- |
| single space                            | Checkbox off       | `[ ]`          |
| single `x`                            | Checkbox on        | `[x]`          |
| single `-`                            | Checkbox mixed     | `[-]`          |
| single `/` or `\`                   | Spinner            | `[/]`          |
| `- N +`                               | Stepper            | `[- 42 +]`     |
| only `=` and `.`                    | Slider/progress    | `[=======...]` |
| only `*` and `.`, 3+ chars          | Rating             | `[***..] 3/5`  |
| touching `[text][v]` or `[text][^]` | Split button       | `[Save][v]`    |
| ends with ` v` or ` ^`              | Accordion/expander | `[FAQ ^]`      |
| single `<` or `>`                   | Previous/next      | `[<]`, `[>]` |
| `#N text`                             | Icon button        | `[#1 Search]`  |
| anything else                           | Button             | `[Submit]`     |

Use a space before accordion chevrons: `[FAQ ^]`, not `[FAQ^]`.

### Angle Brackets

```markui
Name:
<Jane Smith________>

Password:
<****______________>

Date:
<__/__/____>

Country:
<Germany v>
```

- `<@datepicker>` is a custom input.
- `<Label v>` is a collapsed dropdown.
- `<Label ^>` is an expanded dropdown.
- A space before `v` or `^` is required. `<New v2>` is a text input value, not a dropdown.
- Consecutive same-width angle lines become a textarea:

```markui
Description:
<                              >
<                              >
<                              >
```

### Dropdowns

Expanded dropdowns use option lines and close with `->`.

```markui
Fruit:
<Apple ^>
  Apple
  Banana
  Orange
->
```

Multi-select dropdown options can use checkboxes:

```markui
Tags:
<3 selected ^>
  [x] React
  [ ] Vue
  [x] TypeScript
->
```

### Annotations

Only these prefixes are annotations: `(?)`, `($)`, `(!)`, `(i)`, `(x)`, `(v)`.

```markui
Email:
<user@example.com______>
(x) Please enter a valid email.

Password:
<****______________>
(?) Must be at least 8 characters.
```

- Annotations bind to the nearest widget above.
- A blank line breaks annotation binding.
- Any other parenthesized text is a chip/tag, not an annotation.

## Containers

### Closed Box

```markui
+--- Settings --------------------+
|                                  |
|  Dark mode     {[on]/off}        |
|  Language:     <English v>       |
|                                  |
|  [Save]  [Cancel]                |
|                                  |
+----------------------------------+
```

Keep top and bottom borders aligned. Keep content inside the borders.

### Open-Right Box

Open-right boxes do not have a trailing right `+` on the top or bottom border.

```markui
+--- Login ----
|
|  Username:
|  <Jane Doe________>
|
|  Password:
|  <****____________>
|
|  [Login]
|
+----
```

Use open-right syntax for sketchy, left-anchored groups. Use closed boxes when exact containment matters.

### Nested Prefix Boxes

Use `++---` and `+++---` for nested sections. Content does not need leading `|` inside prefix sections.

```markui
+--- Settings ----
|
++--- Profile ----

  Name:
  <Jane Doe________>

++--- Preferences ----

  Dark mode  {[on]/off}

+----
```

### List Containers

Use directional corners for repeatable items.

Vertical list:

```markui
v--- Product ----------------v
| Widget A       $19.99      |
| [Add to cart]              |
v----------------------------v
```

Horizontal list:

```markui
>--- Product --->  >--- Product --->
| Widget A      |  | Widget B      |
| $19.99        |  | $29.99        |
>--------------->  >--------------->
```

Wrapped list:

```markui
w--- Tag --------------------w
| (React) (TypeScript)       |
w----------------------------w
```

Corner summary: `+` structural box, `v` vertical list, `>` horizontal list, `w` wrapped list.

### Scroll, Splitters, and Drop Zones

```markui
+--- Messages -------------------+
| Item 1                         #
| Item 2                         #
+################################+
```

```markui
+----------.----------------------+
| Sidebar  . Main Content         |
| [Nav 1]  . Text                 |
+----------.----------------------+
```

Dashed borders indicate file drop zones:

```markui
+- - - - - - - - - - - -+
|                        |
|   Drag files here      |
|   or [Browse]          |
|                        |
+- - - - - - - - - - - -+
```

## Navigation

### Tabs, Breadcrumbs, Pagination

```markui
+--[[Overview]]--[Details]--[Settings]--+
| Welcome to the overview tab.           |
+----------------------------------------+
```

```markui
Home > Products > Electronics > Laptops
```

```markui
Showing 21-30 of 120 results

[<] 1  2  [[3]]  4  5  ...  12 [>]
```

### Accordion and Expander

Author new accordion bodies as plain content under the open header. Do not draw the old open-right guide `|` and `+--+` in new examples.

```markui
[FAQ ^]
You can return items within 30 days.

[Shipping v]

[Warranty v]
```

- `[Header ^]` is expanded.
- `[Header v]` is collapsed.
- Consecutive sections separated by blank lines form an accordion.
- A single section is an expander.
- The parser tolerates legacy guide markers under open accordions, but generated source should avoid them because they are not intended visible UI.

### Tree View

```markui
- Documents
  - Work
    - Project Alpha
    - Project Beta
  + Personal
+ Downloads
```

`-` means expanded or leaf. `+` means collapsed. Indent controls depth.

### Context Menu

```markui
                   +--------------+
                   | Cut        ^X|
                   | Copy       ^C|
                   | Paste      ^V|
                   | ------------ |
                   | Select All ^A|
                   +--------------+
```

Indent from the left edge signals floating placement.

## Tables

```markui
| [x] | Name  v      | Status  | Actions |
|-----|--------------|---------|---------|
| [ ] | Alice Smith  | Active  | [Edit]  |
| [x] | Bob Johnson  | Pending | [Edit]  |
```

- Use pipes for tables, not boxes.
- A separator row with dashes identifies the header.
- Header suffix `v` means descending sort, `^` means ascending sort.
- `[x]` or `[ ]` can be the selection column.

## Components

### Component References

```markui
+--- Team Members ----------------------+
|                                       |
|  @user-card                           |
|                                       |
|  @user-card                           |
|                                       |
|  [<]   1   [[2]]   3   [>]            |
|                                       |
+---------------------------------------+
```

Put repeated component references on separate rows with breathing room so previews do not overlap.

### Typed Containers

Use `@Type` on a box border for semantic behavior. The type is not the visible title. Put the visible title after the type.

```markui
+--@Modal--- Edit Task ----------------------+
|                                            |
|  Title:                                    |
|  <API v2 endpoints________________>        |
|                                            |
|  [Save]  [Delete]  [Cancel]                |
|                                            |
+--------------------------------------------+
```

```markui
+--@Drawer--- Filters -----------+
|                                |
|  Category:                     |
|  [ ] Electronics               |
|  [x] Books                     |
|                                |
|  Price Range:                  |
|  [========..] $0 - $80         |
|                                |
|  Rating:                       |
|  [***..]          3 stars      |
|                                |
|  [Apply Filters]  [Clear]      |
|                                |
+--------------------------------+
```

### Slots

```markui
{@slot}
{@slot:header}
```

Use slots only in component definitions.

## Images

```markui
!==IMG==!
```

or:

```markui
!=========!
!   IMG   !
!  16:9   !
!=========!
```

When placing controls below an image, leave blank rows. Rendered images occupy visual height even if the ASCII placeholder is short.

## Visual Authoring Guardrails

- Prefer simple valid structures over clever ASCII.
- Keep closed box top and bottom borders the same width.
- For a closed box, start and end content rows with `|`.
- For an open-right box, omit all right-side borders and trailing right `+`.
- Do not leave stray `+---`, `|`, `v---`, `>---`, or `w---` lines that are not valid containers.
- Avoid nested cards inside cards. Use sections, tables, or component refs.
- Put tables inside boxes with margin so table pipes do not merge with box borders.
- Keep image placeholders away from immediately following text or controls.
- Use ASCII hyphens (`-`) and plain quotes in examples. Avoid typographic punctuation.
- If a label might be confused with syntax, add spacing or simplify the text.

## Procedure

1. Identify whether the user needs a `.markui` screen, a Markdown fenced block, or a reusable named component.
2. Choose the main structure: boxless form, closed box, open-right box, table, list container, or typed container.
3. Lay out visible UI only.
4. Add widgets using the bracket rules above.
5. Add annotations only for real visible help, tooltip, warning, info, error, or success messages.
6. Use component refs for repeated blocks instead of duplicating dense nested boxes.
7. If the output lives in this repo, validate it.

## Repo Validation

After changing parser-rendered examples or language docs in this repo:

```powershell
pnpm --filter @jonkeda/markui-core build
node scripts/check-examples.cjs examples
node scripts/check-examples.cjs .my/Language
```

For suspicious visual changes:

```powershell
pnpm visual:gallery
```

For VS Code preview changes:

```powershell
pnpm --filter markui-vscode build:prod
vsce package --allow-missing-repository --no-dependencies --out markui-vscode-0.1.0.vsix
code --install-extension packages\markui-vscode\markui-vscode-0.1.0.vsix --force
```

Reload VS Code after reinstalling the extension.

## Complete Example

```markui
+--- Device Settings --------------------------------------+
|                                                          |
|  ## Device Settings                                      |
|                                                          |
|  Device name:                                            |
|  <Conference Room Display_______>                        |
|                                                          |
|  Network:                                                |
|  <Office Wi-Fi v>                                        |
|                                                          |
|  Brightness: [=======...] 70%                            |
|  Auto-update  {[on]/off}                                 |
|                                                          |
|  ---                                                     |
|                                                          |
|  [Advanced ^]                                            |
|  Update window: <02:00 v>                                |
|  Diagnostics:   [Run Test]                               |
|                                                          |
|  [Save Changes]  [Cancel]                                |
|                                                          |
+----------------------------------------------------------+
```
