# DLSS Updater

A simple desktop tool for finding and updating NVIDIA DLSS files in your application folders, remade in C# for Windows.

The C#/.NET Framework version keeps the executable lightweight at around 140 KB, compared with around 10 MB for the Python build.

![Release](https://img.shields.io/github/v/release/sparepillowgit/dlss-updater-dotnet)
![Last commit](https://img.shields.io/github/last-commit/sparepillowgit/dlss-updater-dotnet)
![License](https://img.shields.io/github/license/sparepillowgit/dlss-updater-dotnet)
![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4)

---

## Links

- Website: https://www.dlssupdater.com
- GitHub: https://github.com/sparepillowgit/dlss-updater-dotnet
- Latest Release: https://github.com/sparepillowgit/dlss-updater-dotnet/releases/latest

---

## Features

- Recursively scans a selected folder
- Detects:
  - `nvngx_dlss.dll`
  - `nvngx_dlssg.dll`
  - `nvngx_dlssd.dll`
- Compares installed versions against the latest supported release
- Updates files only when a newer version is available
- Automatically creates per-file backups before updating
- Choose whether an existing backup is overwritten during an update
- Restore original files from backups at any time

---

## Screenshot

![DLSS Updater Screenshot](Assets/screenshot-01.png)

---

## Download

Download the latest version:

https://github.com/sparepillowgit/dlss-updater-dotnet/releases/latest

- Portable (no installation required)
- Requires .NET Framework 4.8 (included with Windows 10 version 1903 or later and Windows 11)

---

## Usage

1. Launch the application
2. Enter or paste a folder, or click **Browse** to select one
3. Wait for the folder scan to finish
4. Click **Update** to update supported DLSS files

---

## Notes

- Updating protected application folders may require running as **Administrator**
- Only files with a newer available version will be replaced
- Backups are stored beside the original files as `.dubackup.zip` files
- The backup overwrite setting controls whether an existing backup is replaced during an update

---

## FAQ

**What are DLSS files?**  
DLSS files are NVIDIA libraries used by supported applications for features such as AI upscaling, frame generation, and ray reconstruction. Files like `nvngx_dlss.dll`, `nvngx_dlssg.dll`, and `nvngx_dlssd.dll` are typically bundled with applications and can be updated to access newer versions of these features, potentially improving image quality, performance, or compatibility.

**How does DLSS Updater work?**  
DLSS Updater scans a selected folder for supported DLSS files, checks their versions, and lets you update them when newer versions are available.

**"But you can swap DLSS files with the NVIDIA App now, why do I need this?"**  
DLSS Updater is for people who don't want a roughly 170 MB application and its background processes just to manage a few DLSS files. It's a small tool that does one job and then gets out of the way. I'm not a fan of software accumulating features until a simple utility turns into an entire platform. Not everything needs to be bundled into an app that manages your apps, runs in the background, collects usage data, and does a dozen other things you never asked for.

**Why choose this over DLSS Swapper or other similar apps?**  
Other tools offer more features, but DLSS Updater focuses on simplicity. It's a lightweight, portable tool that scans your application folders and updates DLSS files without unnecessary integrations or background processes.

**Does it require installation?**  
No. DLSS Updater is portable and can be run directly from a folder or USB drive.

**How large is the download?**  
The download is around 140 KB. Additional space is required when downloading updated DLSS files.

**Can updating DLSS files break my applications?**  
Yes. Newer DLSS versions aren't guaranteed to work correctly with every application. Updating files can cause compatibility issues, visual glitches, or crashes. Back up your original files before updating so you can restore them if necessary.

**Why did my antivirus flag DLSS Updater?**  
Some antivirus software may flag the tool because downloading and replacing files can resemble behaviour associated with malware. This can trigger heuristic detections, particularly in small utilities distributed as standalone executables. DLSS Updater is open source, so you can inspect its code. To reduce the risk of downloading a modified or malicious copy, obtain it from the official project source and review any antivirus detections rather than automatically assuming they're false positives.

---

## Development

### Build From Source

#### Requirements

- Windows
- Visual Studio with the .NET Framework 4.8 targeting pack, or MSBuild with the targeting pack installed

#### Build Command

Run from a Visual Studio Developer Command Prompt or Developer PowerShell:

```powershell
msbuild dlss-updater.csproj /t:Build /p:Configuration=Release
```

#### Output

The built application will be created in:

```text
bin\Release\
```

Run the executable:

```text
bin\Release\dlss-updater.exe
```
