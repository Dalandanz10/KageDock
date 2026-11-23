# KageDock

KageDock is a customizable desktop dock for Windows. It provides quick access to applications and brings several everyday desktop controls together in one place.

The dock can expand when needed and stay compact when not in use. Applications can be organized into sections and rearranged using drag and drop.

## Features

- **Application management** — Add, remove, organize, and rearrange application shortcuts.
- **Custom sections** — Group applications into sections to keep the dock organized.
- **Drag and drop** — Move applications and reorder sections directly within the interface.
- **Expandable dock** — A compact dock that expands to show its content.
- **Music controls** — Control supported media sessions through Windows media integration.
- **Time Suite** — Built-in timer and stopwatch tools.
- **Appearance settings** — Configure the dock's appearance and behavior.

## Requirements

- Windows 10 version 2004 (build 19041) or later.
- .NET 10 SDK to build the project.
- .NET 10 Desktop Runtime to run a framework-dependent build.

## Building from Source

1. Clone the repository.
2. Open the project directory in a terminal.
3. Restore the dependencies.
4. Build the project.

```powershell
dotnet restore
dotnet build KageDock.csproj -c Release
```

The build output will be available under `bin/Release`.

## Technologies

- C#
- .NET 10
- WPF
- Windows Forms
- SharpVectors.Wpf

## License

All rights reserved. See the [LICENSE](LICENSE) file for details.