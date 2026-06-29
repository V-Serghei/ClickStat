# ClickStat

ClickStat is a Windows desktop app for people who want to understand how they use their keyboard, mouse, applications, words, and gamepads over time. It runs locally, stores data in SQLite, and presents the statistics through a compact WPF interface with live input feedback, charts, heatmaps, themes, and layout-aware views.

![ClickStat overview in light theme](docs/images/overview-light.png)

## Highlights

- **Keyboard analytics**: per-key counters, heatmap-style intensity, typing speed, session input preview, custom/non-standard key tracking, and layout-aware key labels.
- **Mouse analytics**: left/right/middle/back/forward clicks, wheel distance, wheel rotations, scroll direction stats, and custom mouse button shortcuts.
- **Activity dashboard**: daily charts, weekday activity coefficients, hourly heatmaps, cursor distance, and high-level usage summaries.
- **Words and phrases**: frequent words and phrases grouped by installed keyboard layout.
- **Application usage**: input activity grouped by foreground application.
- **Gamepad tracking**: Xbox, PlayStation/DirectInput, and generic controller statistics with per-device history.
- **Input templates**: save snippets, search them with `Alt + D`, paste or copy entries, and capture selected text with `Ctrl + Alt + Shift + D`.
- **Themes and localization**: light/dark themes, Russian/English UI language switching, and JSON-backed translations.
- **Local-first storage**: data is stored on the current machine under `~/Documents/KeyClick/key_statistics.db`.

## Screenshots

The screenshots below are captured from the current English UI. The overview uses the light theme; the other tabs use the dark theme. The Words screenshot is intentionally blurred because that page can contain private typed words and phrases.

### Overview

High-level totals, today's activity, the most frequent key, and a recent activity chart.

![Overview view](docs/images/overview-light.png)

### Keyboard

A full keyboard heatmap with live speed, layout-aware labels, current session input, and non-standard keys.

![Keyboard view](docs/images/keyboard-dark.png)

### Mouse

Mouse button counters, wheel scroll stats, estimated wheel distance, and custom mouse buttons.

![Mouse view](docs/images/mouse-dark.png)

### Activity

Cursor distance, weekday activity coefficients, and an hourly press heatmap.

![Activity view](docs/images/activity-dark.png)

### Words

Frequent words and phrases grouped by detected keyboard layout. The content area is blurred for privacy.

![Words view blurred](docs/images/words-dark.png)

### Apps

Input statistics grouped by foreground application.

![Apps view](docs/images/apps-dark.png)

### Gamepads

Per-device controller history, connection state, buttons, sticks, triggers, and profile detection.

![Gamepads view](docs/images/gamepads-dark.png)

### Settings

Appearance, startup behavior, break reminders, and background image settings.

![Settings view](docs/images/settings-dark.png)

## Core Features

### Keyboard Tracking

ClickStat records keyboard events and aggregates them into readable statistics:

- total key presses and per-key counts;
- color-coded keyboard intensity;
- live CPM-style speed display;
- current keyboard layout display;
- custom key discovery for keys that are not part of the standard visual keyboard;
- a temporary session input field that resets when the window is closed;
- clear, copy, and save actions for the current session input.

### Template Buffer

The template system turns typed or selected text into reusable snippets.

- `Alt + D` opens or closes the template picker.
- `Ctrl + Alt + Shift + D` captures selected text into the template system.
- Templates support search, preview, expand/collapse, paste, copy, and delete.
- Full text can be loaded only when needed, keeping the picker lightweight.

### Mouse Tracking

Mouse statistics include:

- standard buttons: left, right, middle, back, and forward;
- wheel scroll up/down counters;
- wheel notches, rotations, and estimated distance;
- custom mouse buttons and optional shortcut mapping.

### Activity and Words

ClickStat also builds higher-level usage views:

- activity by day and hour;
- weekday distribution;
- most active day;
- frequent words and phrases;
- language tabs based on installed keyboard layouts.

### Gamepads

The gamepad view is designed for both live feedback and long-term history:

- Xbox/XInput controller support;
- PlayStation and DirectInput-style controller support;
- generic HID joystick support;
- per-device button and stick movement totals;
- connected/disconnected state;
- visual controller layout and compact statistics mode.

### Themes, Language, and Settings

The interface supports:

- dark and light themes;
- top-bar icon-only theme toggle;
- theme selection in settings;
- Russian and English UI language switching;
- JSON translation files in `ClickStat.Presentation/Localization`;
- optional launch on Windows startup;
- custom background image.

## Privacy

ClickStat is local-first. It does not need a server to work and stores its data locally in:

```text
~/Documents/KeyClick/key_statistics.db
```

The app records input statistics and optional text templates. This is useful, but it also means the local database can contain sensitive personal data. If you save snippets or capture selected text, that content remains stored locally until you delete it.

## Tech Stack

- **Platform**: Windows desktop
- **UI**: WPF
- **Runtime**: `.NET 10.0 Windows`
- **Storage**: SQLite + Entity Framework Core
- **Charts**: LiveChartsCore + SkiaSharp
- **Input monitoring**: keyboard, mouse, raw input, and gamepad monitoring services
- **Architecture**: App, Presentation, Core, and Infrastructure projects

## Project Structure

```text
ClickStat/
|-- ClickStat.App/              # WPF executable entry point
|-- ClickStat.Presentation/     # Views, view models, localization, themes
|-- ClickStat.Core/             # Core services and shared models
|-- ClickStat.Infrastructure/   # SQLite, data processors, input monitoring
|-- docs/images/                # README screenshots
`-- ClickStat.sln
```

## Getting Started

### Requirements

- Windows
- .NET SDK that can build `net10.0-windows`
- Visual Studio, Rider, or the .NET CLI

### Restore Dependencies

```powershell
dotnet restore ClickStat.sln
```

### Build

```powershell
dotnet build ClickStat.sln --no-restore
```

### Run from Source

```powershell
dotnet run --project ClickStat.App/ClickStat.App.csproj
```

### Run the Compiled Debug Build

```text
ClickStat.App/bin/Debug/net10.0-windows/ClickStat.App.exe
```

### Publish a Local Build

```powershell
dotnet publish ClickStat.App/ClickStat.App.csproj -c Release -r win-x64 --self-contained false
```

The published files will be created under:

```text
ClickStat.App/bin/Release/net10.0-windows/win-x64/publish/
```

## Useful Shortcuts

| Shortcut | Action |
| --- | --- |
| `Alt + D` | Open or close the input template picker |
| `Ctrl + Alt + Shift + D` | Capture selected text into the template system |

## Data Location

Most persisted data lives in:

```text
~/Documents/KeyClick/key_statistics.db
```

UI preferences are also saved under `~/Documents/KeyClick`, including theme and language choices.

## Notes for Contributors

- Keep UI strings in localization JSON files when possible.
- Prefer theme-bound colors for reusable UI surfaces so dark and light themes stay consistent.
- Avoid heavy polling or database work while a tab is not visible.
- Keep input monitoring and UI rendering separated: processors should batch data, view models should expose state, and views should stay presentation-focused.

## Status

ClickStat is under active development. The current UI version shown in the app is `v1.15`.
