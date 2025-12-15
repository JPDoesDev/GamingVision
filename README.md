# GamingVision

An accessibility tool for visually impaired gamers that uses computer vision (YOLO) to detect UI elements in games and reads them aloud using text-to-speech.

## Features

- **Real-time Object Detection**: Uses YOLOv8 models via ONNX Runtime with DirectML GPU acceleration
- **Three-Tier Detection System**: Primary (auto-read), Secondary (on-demand), and Tertiary (on-demand) object categories
- **Text-to-Speech**: Windows SAPI voices with configurable voice and speed per tier
- **OCR Integration**: Windows.Media.Ocr for extracting text from detected regions
- **Global Hotkeys**: Control the app while gaming without alt-tabbing
- **Per-Game Profiles**: Each game has its own folder with model, labels, and configuration
- **Accessibility-First UI**: High contrast support, screen reader compatible
- **Debug Logging**: Optional logging to file for troubleshooting

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
src\GamingVision\bin\Release\net8.0-windows10.0.22621.0\win-x64\GamingVision.exe
```

### 2. Export Your YOLO Model

If you have a trained YOLOv8 `.pt` model, convert it to ONNX:

```powershell
py -3.10 export_model.py
```

This exports `NoMansAccess/NoMansModel.pt` to `GameModels/no_mans_sky/`.

### 3. Run the Application

```powershell
dotnet run -c Release --project src\GamingVision
```

Or run `GamingVision.exe` directly.

## Default Hotkeys

| Hotkey | Action |
|--------|--------|
| Alt+1 | Read primary objects (auto-read targets) |
| Alt+2 | Read secondary objects (on-demand) |
| Alt+3 | Read tertiary objects (on-demand) |
| Alt+4 | Stop reading |
| Alt+5 | Toggle detection on/off |
| Alt+Q | Quit application |

All hotkeys are configurable per game in Game Settings.

## Project Structure

```
GamingVision/
├── src/GamingVision/
│   ├── Models/              # Data models (GameProfile, Settings, etc.)
│   ├── ViewModels/          # MVVM ViewModels
│   ├── Views/               # WPF Windows and dialogs
│   ├── Services/
│   │   ├── Detection/       # YOLO detection service + DetectionManager
│   │   ├── Ocr/             # Windows OCR service
│   │   ├── Tts/             # Text-to-speech service
│   │   ├── Hotkeys/         # Global hotkey service
│   │   └── ScreenCapture/   # Windows Graphics Capture
│   ├── Utilities/           # ConfigManager, Logger, GPU detection
│   └── Native/              # Win32 P/Invoke declarations
├── GameModels/              # Per-game model folders
│   └── no_mans_sky/
│       ├── NoMansModel.onnx # YOLO model
│       ├── NoMansModel.txt  # Label definitions
│       └── game_config.json # Game-specific configuration
├── app_settings.json        # Application-wide settings
├── export_model.py          # Script to convert .pt to .onnx
└── DESIGN_DOCUMENT.md       # Technical design specification
```

## Configuration

### Application Settings (app_settings.json)

Located in the application directory:

```json
{
  "version": "1.0",
  "selectedGame": "no_mans_sky",
  "useDirectML": true,
  "autoStartDetection": false,
  "minimizeToTray": false,
  "enableLogging": false,
  "logFilePath": "logs/gamingvision.log"
}
```

### Per-Game Settings (GameModels/{game}/game_config.json)

Each game has its own configuration file:

```json
{
  "gameId": "no_mans_sky",
  "displayName": "No Man's Sky",
  "modelFile": "NoMansModel.onnx",
  "windowTitle": "No Man's Sky",
  "primaryLabels": ["title", "item"],
  "secondaryLabels": ["info", "quest"],
  "tertiaryLabels": ["controls", "menu"],
  "labelPriority": ["title", "item", "info", "quest", "controls", "menu"],
  "hotkeys": {
    "readPrimary": "Alt+1",
    "readSecondary": "Alt+2",
    "readTertiary": "Alt+3",
    "stopReading": "Alt+4",
    "toggleDetection": "Alt+5",
    "quit": "Alt+Q"
  },
  "capture": {
    "method": "fullscreen",
    "monitorIndex": 0
  },
  "tts": {
    "engine": "sapi",
    "primaryVoice": "Microsoft David Desktop",
    "primaryRate": 3,
    "secondaryVoice": "Microsoft Zira Desktop",
    "secondaryRate": 0,
    "tertiaryVoice": "Microsoft Zira Desktop",
    "tertiaryRate": 0,
    "volume": 100
  },
  "detection": {
    "autoReadCooldown": 1000,
    "confidenceThreshold": 0.3,
    "autoReadConfidenceThreshold": 0.6,
    "autoReadEnabled": false,
    "readPrimaryLabelAloud": true,
    "readSecondaryLabelAloud": false,
    "readTertiaryLabelAloud": false
  }
}
```

### Detection Settings Explained

- **confidenceThreshold**: Minimum confidence for manual hotkey reads (0.3 = 30%)
- **autoReadConfidenceThreshold**: Higher threshold for auto-read to reduce false positives (0.6 = 60%)
- **autoReadEnabled**: Whether primary objects are automatically read when detected
- **readPrimaryLabelAloud**: Include the label name when speaking (e.g., "item, Health Pack")

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
6. Create a `game_config.json` with your settings
7. Place all files in `GameModels/{your_game}/`

## Debugging

### Enable Logging

1. Open **App Settings** from the main window
2. Check **"Enable Logging"**
3. Click **Save**

Logs are written to `logs/gamingvision.log` in the application directory.

### Crash Logs

If the application crashes, check `crash_log.txt` in the application directory for details.

## Technology Stack

- **Framework**: .NET 8, WPF
- **MVVM**: CommunityToolkit.Mvvm
- **ML Inference**: ONNX Runtime 1.16.3 with DirectML
- **Screen Capture**: Windows.Graphics.Capture API
- **OCR**: Windows.Media.Ocr (WinRT)
- **TTS**: System.Speech (Windows SAPI)
- **GPU Detection**: System.Management (WMI)

## Known Limitations

- Windows only (uses Windows-specific APIs)
- Requires a trained YOLO model for each game
- Some games with anti-cheat may block screen capture
- OCR accuracy depends on game font clarity
- ONNX inference is serialized (one frame at a time) to prevent GPU crashes

## License

MIT License - See LICENSE file for details.

## Credits

- Built on the foundation of NoMansAccess Python project
- Uses [Ultralytics YOLOv8](https://github.com/ultralytics/ultralytics) for object detection
- Uses [ONNX Runtime](https://onnxruntime.ai/) for inference
