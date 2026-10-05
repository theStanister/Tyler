# Tyler — v1.0.0.0

## Release v1.0.0.0

This is the initial release of **Tyler**, a Windows desktop application that turns very tall screenshots into printable, multi-column, multi-page PDFs.

**Tyler** is the image *Tiler* for screenshots. It takes a long vertical strip (for example, a long chat conversation or a full-page screenshot), slices it into overlapping vertical tiles, and lays them out column-by-column across pages. The result is a tidy, multi-page PDF where each page holds several tiles side by side — so a long screenshot stays readable on paper without being shrunk to an illegible size.

The name is a pun: Tyler is a **Tiler** for screenshots.

## What's New / Features

- **Native Windows GUI** — a modern, dark-themed GUI built directly on the Win32 API (no WinForms / WPF / Avalonia). Drag-and-drop or browse for an input image, set your options, and convert with a single click.
- **Command-line interface (CLI)** — the same conversion engine is available from the terminal for scripting and unattended use.
- **Tall-screenshot tiling** — slices a long image into overlapping vertical strips and arranges them column-by-column, page-by-page, so nothing is clipped at tile seams.
- **Configurable output** — choose **paper size** (A4 / Letter) and **orientation** (Portrait / Landscape).
- **Layout controls** — adjustable **columns per page**, **page margin** (mm), and **vertical tile overlap** (0–100%).
- **Self-contained native executable** — built with .NET 10 **Native AOT**. No .NET runtime and no system fonts are required.
- **Single-install deployment** — the installer bundles the executable plus QuestPDF's support files (`qpdf.dll`, `QuestPdfSkia.dll`, `QuestPDF.Fonts.Lato.br`).

## Installer Assets

The following asset is attached to this release and available for download:

| File | Description |
|------|-------------|
| [`Tyler-1.0.0.0-x64.msi`](./Tyler-1.0.0.0-x64.msi) | Windows x64 installer (MSI, per-machine) |

A SHA-256 checksum is provided alongside the installer:

| File | SHA-256 Checksum |
|------|------------------|
| `Tyler-1.0.0.0-x64.msi.sha256` | `AF6F02FFFDA6A9BEBE1FA4E32E9CD9E6944258EBAF1D416E0F70A5F30DF8B0B7` |

## Installation

1. Download [`Tyler-1.0.0.0-x64.msi`](./Tyler-1.0.0.0-x64.msi).
2. Double-click the file to run the installer (administrator rights are required because it is a per-machine install), or run it from a command prompt:
   ```
   msiexec /i Tyler-1.0.0.0-x64.msi
   ```
3. Follow the on-screen prompts. The installer places **Tyler** under `Program Files`, creates a **Start Menu** shortcut, and registers the app for uninstall via **Add/Remove Programs**.
4. Launch **Tyler** from the Start Menu shortcut (opens the GUI) or run `Tyler.exe` from the command line for CLI use.

## Requirements / Prerequisites

- **Operating system:** 64-bit Windows (x64). Windows 10 or Windows 11 recommended.
- **No .NET runtime required** — the application is fully self-contained (Native AOT).
- **No system fonts required** — the bundled Lato font data is used for PDF generation.
- **Administrator privileges** are needed to install the per-machine MSI.

## Verifying the Download

To verify the integrity of the downloaded installer, compute its SHA-256 hash and compare it with the value above:

```
certutil -hashfile Tyler-1.0.0.0-x64.msi SHA256
```

## License

Tyler is released under the [MIT License](../LICENSE).
