# RazerProlapse

Lightweight C# WPF (.NET 9.0) desktop utility to read and set Razer DeathAdder mouse DPI directly over USB HID without Razer Synapse.

## Features
- Direct USB HID communication (`VID 0x1532`) via `HidSharp` and Win32 `HidD_SetFeature` / `HidD_GetFeature`
- Supports both legacy `Set DPI` (`0x05`) and modern `Set DPI Stages` (`0x26`) firmware commands
- Reads current mouse DPI on startup and saves changes to mouse hardware memory
- Quick presets (`400`, `800`, `1600`, `3200`) and smooth `100–16000` DPI slider

## Build & Publish
```powershell
dotnet build
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```
