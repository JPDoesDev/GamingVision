# VIGamingVision

An accessibility tool for visually impaired gamers that uses computer vision (YOLO) to detect UI elements in games and reads them aloud using text-to-speech.

## Features

- **Real-time Object Detection**: Uses YOLOv8 models via ONNX Runtime with DirectML GPU acceleration
- **Text-to-Speech**: Windows SAPI voices with configurable speed and volume
- **OCR Integration**: Windows.Media.Ocr for extracting text from detected regions
- **Global Hotkeys**: Control the app while gaming without alt-tabbing
- **Per-Game Profiles**: Each game can have its own model, hotkeys, and settings
- **Accessibility-First UI**: High contrast support, screen reader compatible

## Requirements

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- GPU with DirectML support (NVIDIA, AMD, or Intel) - falls back to CPU if unavailable
- Python 3.10-3.12 (only for model export)

## Quick Start

### 1. Build the Application

```powershell
dotnet build -c Release
```

The executable will be at:
```
src\VIGamingVision\bin\Release\net8.0-windows10.0.22621.0\VIGamingVision.exe
```

### 2. Export Your YOLO Model

If you have a trained YOLOv8 `.pt` model, convert it to ONNX:

```powershell
py -3.10 export_model.py
```

This exports `NoMansAccess/NoMansModel.pt` to the `models/` folder.

### 3. Run the Application

```powershell
dotnet run -c Release --project src\VIGamingVision
```

Or run `VIGamingVision.exe` directly.

## Default Hotkeys

| Hotkey | Action |
|--------|--------|
| Alt+1 | Read primary objects (auto-read targets) |
| Alt+2 | Read secondary objects (on-demand only) |
| Alt+3 | Stop reading |
| Alt+4 | Toggle detection on/off |
| Alt+Q | Quit application |

All hotkeys are configurable per game in Game Settings.

## Project Structure

```
VIGamingVision/
├── src/VIGamingVision/
│   ├── Models/              # Data models (GameProfile, Settings, etc.)
│   ├── ViewModels/          # MVVM ViewModels
│   ├── Views/               # WPF Windows and dialogs
│   ├── Services/
│   │   ├── Detection/       # YOLO detection service
│   │   ├── Ocr/             # Windows OCR service
│   │   ├── Tts/             # Text-to-speech service
│   │   ├── Hotkeys/         # Global hotkey service
│   │   └── ScreenCapture/   # Windows Graphics Capture
│   ├── Utilities/           # Config manager, GPU detection
│   └── Native/              # Win32 P/Invoke declarations
├── NoMansAccess/            # Original Python project with trained model
├── export_model.py          # Script to convert .pt to .onnx
└── DESIGN_DOCUMENT.md       # Technical design specification
```

## Configuration

Settings are stored in `config.json` next to the executable. On first run, a default configuration is created with a No Man's Sky profile.

### Game Profile Settings

Each game profile includes:

- **Display Name**: Friendly name shown in the UI
- **Model File**: Path to the ONNX model (e.g., `models/nomanssky.onnx`)
- **Window Title**: Game window to capture (partial match)
- **Primary Labels**: Object types that trigger auto-read
- **Secondary Labels**: Object types read only on hotkey
- **Hotkeys**: Per-game hotkey bindings
- **TTS Settings**: Voice, speed, volume per game
- **Detection Settings**: Confidence threshold, cooldown, FPS

### Application Settings

- **UseDirectML**: Enable GPU acceleration (default: true)
- **AutoStartDetection**: Start detecting when game is selected
- **MinimizeToTray**: Minimize to system tray on close
- **EnableLogging**: Write debug logs to file

## Creating Custom Models

1. Collect screenshots from your target game
2. Label UI elements using a tool like [Label Studio](https://labelstud.io/) or [CVAT](https://cvat.ai/)
3. Train a YOLOv8 model:
   ```python
   from ultralytics import YOLO
   model = YOLO('yolov8n.pt')
   model.train(data='your_dataset.yaml', epochs=100)
   ```
4. Export to ONNX:
   ```python
   model.export(format='onnx', imgsz=640, simplify=True)
   ```
5. Create a labels file (`modelname.txt`) with one class per line
6. Place both files in the `models/` folder

## Technology Stack

- **Framework**: .NET 8, WPF
- **MVVM**: CommunityToolkit.Mvvm
- **ML Inference**: ONNX Runtime with DirectML
- **Screen Capture**: Windows.Graphics.Capture API
- **OCR**: Windows.Media.Ocr (WinRT)
- **TTS**: System.Speech (Windows SAPI)
- **GPU Detection**: System.Management (WMI)

## Known Limitations

- Windows only (uses Windows-specific APIs)
- Requires a trained YOLO model for each game
- Some games with anti-cheat may block screen capture
- OCR accuracy depends on game font clarity

## License

MIT License - See LICENSE file for details.

## Credits

- Built on the foundation of [NoMansAccess](NoMansAccess/) Python project
- Uses [Ultralytics YOLOv8](https://github.com/ultralytics/ultralytics) for object detection
- Uses [ONNX Runtime](https://onnxruntime.ai/) for inference
