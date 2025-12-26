# ZeroTouch.UI

A modern **Avalonia-based UI** for the ZeroTouch project.

---

<br>

## Setup & Development Guide

### 1. Prerequisites

Before setting up the project, ensure the following tools are properly installed on your system:

<br>

Required

- **.NET SDK 8.0+** [Download .NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download)

    Choose Windows x64 Installer and follow the setup instructions.

    After installation, verify your installation by running:
        
    ```bash
    dotnet --version
    ```

<br>

- **Visual Studio 2022** (or later)  
    
    Workload required:

    - .NET desktop development
    - WinUI application development
    - Git for Windows

    After completing the setup, restart Visual Studio to ensure all components are properly loaded.

<br>

- **Avalonia**

    To enable Avalonia project templates and the XAML previewer in Visual Studio:

    1. Open Visual Studio.

    2. From the top menu, navigate to:
    Extensions → Manage Extensions

    3. In the search bar, type “Avalonia”.

    4. Install the following extensions:

        - Avalonia for Visual Studio
        - Avalonia XAML Previewer (automatically installed together)

    Once installation completes, restart Visual Studio.

<br>

---

<br>

### 2. Clone the Repository

```bash
git clone https://github.com/nuk-zerotouch/zerotouch.git

cd ZeroTouch.UI
```

<br>

### 3. Restore Dependencies

After cloning, restore NuGet packages before building:

```bash
dotnet restore
```

<br>

### 4. Build the Project

Build using command line or Visual Studio:

```bash
dotnet build
```

<br>

If seeing:

```bash
error NETSDK1004: Assets file '..\project.assets.json' not found. Run a NuGet package restore to generate this file.
```

<br>

It means dependencies were not restored. Please re-run: 

```bash
dotnet restore
```

<br>

### Run the Application

To start the Avalonia UI app:

```bash
dotnet run --project ZeroTouch.UI
```

Or simply press `F5` in Visual Studio to start debugging.

<br>

## Running on Jetson Orin Nano (ARM64)

When deploying ZeroTouch.UI on **Jetson Orin Nano (ARM64)**, you may encounter runtime errors related to native dependencies (e.g. SkiaSharp symbol resolution).

To ensure correct execution, **do not run `dotnet run` directly**.  
Instead, use the provided helper script:

```bash
./scripts/run-jetson.sh
```

If this is your first time running the script, make sure it is executable:

```bash
chmod +x scripts/run-jetson.sh
```

This script sets required environment variables (e.g. LD_PRELOAD) before launching the application.

For details about the root cause and the workaround, see the 
[docs/deployment/jetson.md](docs/deployment/jetson.md).

<br>

## Project Structure

```bash
ZeroTouch.UI/
├── Assets/                  # Icons, images, and static files
│   ├── Icons/
│   │   ├── Dark/
│   │   └── Light/
│   └── avalonia-logo.ico
│
├── Models/                  # Data models
├── Services/                # WebSocket client and background logic
├── ViewModels/              # MVVM view models
├── Views/                   # XAML UI files
├── App.axaml                # Application entry (Avalonia)
├── ATTRIBUTIONS.txt         # Licensing and credits
└── README.md
```

<br>

## Development Workflow

Pull the latest changes:

```bash
git pull
```

<br>

Create a new branch for your feature or fix:

```bash
git checkout -b feat/gesture-debug-ui
```

<br>

Commit following Conventional Commits:

Example: 
```bash
git commit -m "feat(ui): add navigation sidebar with icons"
```

<br>

Push your branch and open a pull request:

```bash
git push origin feat/gesture-debug-ui
```

<br>

## Licensing and Attribution

This project is licensed under <a href="LICENSE">MIT license</a>.

<br>

All icon and image sources are credited in <a href="ATTRIBUTIONS.txt">ATTRIBUTIONS.txt</a>  at the project root. Please update this file whenever you add new licensed assets.

<br>

> Developed with ❤️ by the ZeroTouch Team
