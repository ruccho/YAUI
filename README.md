<div align="center">

<h1>YAUI</h1>

<p>
<strong>
Yet Another Unity UI: a fast, Flexbox-based UI system for Unity,<br>built on GameObjects.
</strong>
</p>

[![Releases](https://img.shields.io/github/release/ruccho/YAUI.svg)](https://github.com/ruccho/YAUI/releases)

English | [日本語](README.ja.md)


<img src="docs/static/img/hero.png" alt="YAUI - Yet Another Unity UI" width="800">
</div>

## Why YAUI?

Unity's uGUI works with Prefabs, Animator and the Inspector, but is slow: meshes are rebuilt and batched on the main thread, layout groups are expensive, and custom shaders break batching. UI Toolkit is fast, but it is separate from GameObjects.

YAUI keeps the GameObject authoring model and drops compatibility with uGUI to get the performance of a modern UI engine.

<img src="docs/static/img/screenshot.png" width="800">

### Fast

Every box, image and glyph is a quad in a GPU buffer, drawn in **one draw call per panel**. Rounded corners, borders, drop shadows and SDF text are all drawn by one uber shader, without breaking the batch. Text generation and layout run as **jobs on worker threads**, and a frame without changes costs almost nothing.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/static/img/benchmark-dark.svg">
  <img src="docs/static/img/benchmark-light.svg" alt="Time added by the UI per frame on a Pixel 5: YAUI, uGUI and UI Toolkit" width="800">
</picture>

The same screens built with YAUI, uGUI and UI Toolkit, measured on a Pixel 5. YAUI has the lowest main thread cost in most scenarios; its GPU cost is higher than uGUI's. See the [benchmarks](https://ruccho.com/YAUI/benchmarks) for the details.

### Advanced layouts

**Flexbox** layout with a port of Yoga, computed with Burst. Boxes with a fixed size are **layout boundaries**, so a change only lays out the part of the tree it affects.

### GameObject-based

Elements are components on GameObjects: use **Prefabs, Animator, Timeline and the Inspector** as usual. Input goes through the **EventSystem**, and panels can be drawn over the screen or in **world space**.

> [!NOTE]
> YAUI is **experimental**. Breaking changes can be made before the first stable release.

## Requirements

- Unity 6000.7 (Unity 6.7) or later
- Universal Render Pipeline (URP)
- Vulkan, Metal or Direct3D 11 / 12 (OpenGL ES is not supported)

## Installation

Open the Package Manager, choose **Install package from git URL...** and enter:

```
https://github.com/ruccho/YAUI.git?path=/Packages/com.ruccho.yaui#release
```

## Documentation

### See the [documentation](https://ruccho.com/YAUI) for the manual and the API reference.

## License

[MIT](LICENSE). The flex layout engine is based on the C# port of Yoga in Microsoft.UI.Reactor; see [Third Party Notices](Packages/com.ruccho.yaui/Third%20Party%20Notices.md).
