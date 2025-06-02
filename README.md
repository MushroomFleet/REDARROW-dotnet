# You Are Here - Pointer Tool for Live Broadcasting

A simple yet powerful red arrow pointer tool designed specifically for live broadcasters, presenters, and educators. Based on the SharkFin Companion architecture, this tool provides an easy way to point out specific areas on your screen during presentations or live streams.

## Features

### Simple Click-to-Move
- **Left Click**: Move the red arrow to any clicked position on screen
- Smooth animation with automatic rotation to point in the direction of movement
- Smart duration calculation based on distance

### Park/Unpark System
- **Ctrl + Left Click**: Move arrow to position AND park it there
- Parked arrows stay fixed until manually unparked
- **Ctrl + Left Click (when parked)**: Unpark the arrow to resume normal behavior
- Visual indication when parked (pulsing animation)

### Broadcasting-Friendly Design
- Semi-transparent red arrow with drop shadow for excellent visibility
- Always stays on top of other windows
- Click-through window design - won't interfere with other applications
- Professional appearance suitable for live streaming

## How to Use

1. **Run the Application**: Launch `YouAreHereApp.exe`
2. **System Tray**: The app runs in the system tray with a red arrow icon
3. **Basic Movement**: Left-click anywhere on screen to move the arrow there
4. **Park Arrow**: Hold Ctrl + Left-click to move and park the arrow
5. **Unpark Arrow**: When parked, Ctrl + Left-click again to unpark
6. **Exit**: Right-click the system tray icon and select "Exit"

## System Tray Features

- **Left-click tray icon**: Show current status and position
- **Right-click tray icon**: Access context menu with:
  - Instructions
  - Reset to Center
  - Exit

## Technical Requirements

- Windows 10/11
- .NET 6.0 or later
- Supports multi-monitor setups

## Building from Source

```bash
cd you-are-here
dotnet build
dotnet run
```

## Use Cases

Perfect for:
- **Live Streaming**: Point out UI elements, highlight important areas
- **Presentations**: Draw attention to specific content
- **Tutorials**: Guide viewers through software interfaces
- **Remote Teaching**: Emphasize key information on screen
- **Video Recording**: Create professional-looking instructional content

## Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| Move arrow | Left Click |
| Park/Unpark | Ctrl + Left Click |
| Reset to center | Via system tray menu |

## Settings

Settings are automatically saved to:
`%APPDATA%\YouAreHere\settings.json`

Current settings include:
- Animation speed
- Arrow size
- Auto-start preferences

## Architecture

Built using:
- **WPF** for smooth graphics and animations
- **Global Mouse Hooks** for system-wide click detection
- **System Tray Integration** for minimal interface
- **JSON Configuration** for persistent settings

## License

Based on the SharkFin Companion project architecture.
