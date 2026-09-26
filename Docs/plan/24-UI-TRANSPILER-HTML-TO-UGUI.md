# 24 — UI Transpiler: HTML/TS → Unity uGUI

> User requirement: *"if you can actually make a script that can convert TypeScript UI or HTML UI
> to Unity GUI, so if later I can ask another agent to redesign a reference I want and send to you
> to make for us."*

## 1. What this is for

A designer (human or agent) iterates on a screen in HTML/CSS — fast, previewable in a browser,
easy to share. The transpiler converts that into a **real Unity uGUI hierarchy** built from our
component library (doc 20), not a WebView and not a screenshot.

It is a **design-time tool**, not a runtime dependency. Nothing in the shipping game depends on it.

## 2. Non-goals (stated up front so the tool stays honest)

- Not a general HTML/CSS engine. It supports a **documented subset**.
- Not pixel-perfect for arbitrary CSS. It maps *layout intent* onto uGUI layout groups.
- Not a replacement for the design system. Output is expressed in **our components**; unknown
  constructs fail loudly rather than approximating badly.

## 3. Pipeline

```
 source.html + theme.css
        │
        ▼
 1. PARSE      HTML → DOM (node-html-parser), CSS → rules (postcss)
 2. RESOLVE    cascade + inheritance → a computed style per node
 3. MEASURE    headless Chromium (already available, doc 07) lays the page out at
               1920×1080 and reports each node's computed box + font metrics
 4. MAP        DOM node + computed style → a UINode IR
 5. MATCH      map to library components via data-attributes or heuristics
 6. VALIDATE   every colour/size must resolve to a design token, or error
 7. EMIT       UIDocument.json  (the IR)
 8. BUILD      Unity editor importer reads the IR and constructs the hierarchy
```

Step 3 is the trick that makes this tractable: **we let a real browser do layout**, then translate
the *result*. We are not reimplementing flexbox.

## 4. The IR (`UIDocument.json`)

```jsonc
{
  "version": 1,
  "reference": { "width": 1920, "height": 1080 },
  "root": {
    "name": "PlayTab",
    "component": "Panel",
    "rect": { "anchorMin": [0,0], "anchorMax": [1,1], "offsetMin": [48,56], "offsetMax": [-48,-88] },
    "layout": { "type": "vertical", "spacing": 24, "padding": [0,0,0,0], "childAlign": "upperLeft" },
    "style": { "bg": "surface/card", "border": "line/strong", "radius": 2 },
    "children": [
      { "name": "Title", "component": "Text",
        "text": { "loc": "play.title", "style": "h1", "color": "text/primary" } },
      { "name": "QuickPlay", "component": "Card",
        "props": { "title": "play.quick.title", "subtitle": "play.quick.sub", "icon": "icon/quickplay" },
        "focus": { "id": "quickplay", "right": "host", "down": "solo" },
        "action": "Play.QuickPlay" }
    ]
  }
}
```

The IR is **the contract**. Anything that can emit this JSON can build Unity UI — HTML is just the
first front-end for it. A Figma exporter or a hand-written JSON file works identically.

## 5. Mapping table

| HTML / CSS | uGUI |
|---|---|
| `<div>` | `RectTransform` (+ `Image` if it has a background) |
| `display:flex; flex-direction:row/column` | `HorizontalLayoutGroup` / `VerticalLayoutGroup` |
| `display:grid` | `GridLayoutGroup` (uniform) or explicit anchors (non-uniform) |
| `gap` | `spacing` |
| `padding` | `padding` |
| `justify-content` / `align-items` | `childAlignment` + `childForceExpand` |
| `position:absolute` | explicit anchors + offsets from the measured box |
| `width/height: %` / `px` | anchors / `sizeDelta` |
| `flex: 1` | `LayoutElement.flexibleWidth/Height` |
| `<p> <span> <h1>` | `TextMeshProUGUI` with the matching type token |
| `<img>` | `Image` with a sprite reference |
| `<button>` | `Button` from the library |
| `<input>` | `TMP_InputField` |
| `overflow:auto` | `ScrollRect` + `Mask` + `Scroller` |
| `background-color` | `Image.color` — **must** resolve to a token |
| `border` | sliced sprite or `Outline` |
| `border-radius` | rounded sprite variant (0 / 2 / pill only) |
| `opacity` | `CanvasGroup.alpha` |
| `transform` | `RectTransform` scale/rotation |
| `:hover` / `:focus` | component state colours (from the theme, not from CSS) |
| `@media` | ignored — we target one reference resolution and scale |

## 6. Authoring contract (`data-*` attributes)

Explicit beats inferred. Designers annotate intent:

```html
<div data-ui="Card"
     data-props='{"title":"play.quick.title","icon":"icon/quickplay"}'
     data-focus="quickplay" data-focus-right="host" data-focus-down="solo"
     data-action="Play.QuickPlay">…</div>

<span data-ui="Text" data-style="h1" data-loc="play.title">PLAY</span>
<div  data-ui="StatBar" data-bind="weapon.stats"></div>
<div  data-ui="3DViewport" data-bind="operator.preview"></div>
```

| Attribute | Meaning |
|---|---|
| `data-ui` | Which library component to emit |
| `data-props` | JSON props for that component |
| `data-style` | Type/visual token |
| `data-loc` | Localisation key (required for all user-visible text) |
| `data-focus*` | Focus graph id and neighbours (doc 08 §7) |
| `data-action` | The handler id bound at build time |
| `data-bind` | A data source for dynamic content |

## 7. Token enforcement

Step 6 is a hard gate: a raw `#ff7a1a` in the CSS is an **error**, with a suggestion
(`did you mean accent/primary?`). `theme.css` is **generated from `UIThemeAsset`**, so the browser
preview and the game are guaranteed to use the same values. A single source of truth for colour,
type and spacing, in both worlds.

## 8. Handler binding

`data-action="Play.QuickPlay"` maps to a method in a registry:

```csharp
[UIAction("Play.QuickPlay")] void OnQuickPlay() { … }
```
The importer wires listeners by id. Missing handlers are a build error with the list of what's
missing — the UI can never silently do nothing.

## 9. Round-trip preview

Because the Unity Web Clone (doc 06) can render our uGUI hierarchies, we can show the **HTML
source** and the **transpiled Unity result** side by side in one image and diff them. That is how
the transpiler earns trust: not "it should match", but a diff image.

```bash
npm run transpile -- designs/play.html --out Assets/UI/Generated/PlayTab.uidoc.json
npm run transpile:diff -- designs/play.html    # → Artifacts/ui-diff/play.png
```

## 10. Unity side

- `Assets/Scripts/Editor/UIDocumentImporter.cs` — a `ScriptedImporter` for `*.uidoc.json`.
- Menu: `COD / UI / Build Screen From Document`.
- **Idempotent:** rebuilding updates the existing hierarchy in place, preserving any manual
  additions marked with a `[KeepOnRebuild]` component. Designers can iterate without losing work.
- Output is **plain saved scene objects** (doc 21 §2) — inspectable and editable, consistent with
  the menu-in-scene rule.

## 11. Limits, documented honestly

Unsupported and reported as errors, not silently wrong: floats, tables, CSS grid with non-uniform
tracks, pseudo-element content, filters/blend modes, animations (use the theme's motion tokens),
and web fonts (must map to a project font).

## 12. Acceptance (P6)

- [ ] Round-trips the PLAY tab from HTML to a working, pad-navigable Unity screen.
- [ ] Token enforcement rejects a raw hex colour with a helpful message.
- [ ] Missing handler or missing loc key fails the build with a precise list.
- [ ] Diff image between the HTML render and the Unity render shows < 3% pixel difference on the
      reference layout.
- [ ] Rebuilding a screen preserves `[KeepOnRebuild]` additions.
