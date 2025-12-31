# GamingVision

An accessibility tool for visually impaired gamers that uses computer vision (YOLO) to detect UI elements in games and reads them aloud using text-to-speech.

## Features

- **Real-time Object Detection**: Uses YOLOv11 models via ONNX Runtime with DirectML GPU acceleration
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

If you have a trained YOLOv11 `.pt` model, convert it to ONNX:

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
├── src/
│   ├── GamingVision/                    # Main WPF application
│   │   ├── Models/                      # Data models (GameProfile, Settings, etc.)
│   │   ├── ViewModels/                  # MVVM ViewModels
│   │   ├── Views/                       # WPF Windows and dialogs
│   │   ├── Services/
│   │   │   ├── Detection/               # YOLO detection service + DetectionManager
│   │   │   ├── Ocr/                     # Windows OCR service
│   │   │   ├── Tts/                     # Text-to-speech service
│   │   │   ├── Hotkeys/                 # Global hotkey service
│   │   │   └── ScreenCapture/           # Windows Graphics Capture
│   │   ├── Utilities/                   # ConfigManager, Logger, GPU detection
│   │   └── Native/                      # Win32 P/Invoke declarations
│   │
│   └── GamingVision.TrainingTool/       # Training data collection console app
│       ├── Program.cs                   # TUI menu and capture logic
│       ├── TrainingDataManager.cs       # YOLO annotation file management
│       └── ConsoleHotkeyService.cs      # F1 hotkey handling
│
├── GameModels/                          # Per-game model folders
│   └── no_mans_sky/
│       ├── NoMansModel.onnx             # YOLO model
│       ├── NoMansModel.txt              # Label definitions
│       └── game_config.json             # Game-specific configuration
│
├── training_data/                       # Training data output (created by TrainingTool)
│   └── {game_id}/
│       ├── images/                      # Screenshots (PNG)
│       ├── labels/                      # YOLO annotations (TXT)
│       └── classes.txt                  # Label names for LabelImg
│
├── app_settings.json                    # Application-wide settings
├── export_model.py                      # Script to convert .pt to .onnx
└── DESIGN_DOCUMENT.md                   # Technical design specification
```

## Configuration

### Application Settings (app_settings.json)

Located in the application directory:

```json
{
  "version": "0.2.1",
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

## Training Data Collection Tool

GamingVision includes a console-based training data collection tool for creating and improving YOLO models.

### Running the Training Tool

```powershell
dotnet run --project src\GamingVision.TrainingTool
```

### Features

- **TUI Menu**: Select existing game profile or create a new one
- **F1 Hotkey**: Captures fullscreen screenshot while gaming
- **Auto-Detection**: If a model exists, runs YOLO and pre-labels the screenshot
- **LabelImg Compatible**: Outputs YOLO format annotations

### Workflow

1. Run the training tool and select a game (or create new profile)
2. Launch your game
3. Press **F1** whenever there's an interesting UI element on screen
4. Screenshots are saved to `training_data/{game}/images/`
5. If model exists, annotations are saved to `training_data/{game}/labels/`
6. Press **Escape** to return to menu
7. Use [LabelImg](https://github.com/HumanSignal/labelImg) to review/adjust annotations
8. Train your model with the collected data

### Output Format

```
training_data/{game_id}/
  images/screenshot_0001.png
  labels/screenshot_0001.txt   # Only if detections found
  classes.txt                  # Label names for LabelImg
```

Annotation format (YOLO): `class_id center_x center_y width height` (normalized 0-1)

## Creating Custom Models

1. Use the Training Tool to collect screenshots from your target game
2. Label UI elements using [LabelImg](https://github.com/HumanSignal/labelImg) (annotations in `training_data/{game}/labels/`)
3. Train a YOLOv11 model:
   ```python
   from ultralytics import YOLO
   model = YOLO('yolo11n.pt')
   model.train(data='your_dataset.yaml', epochs=100)
   ```
4. Export to ONNX:
   ```python
   model.export(format='onnx', imgsz=640, simplify=True)
   ```
5. Create a labels file (`modelname.txt`) with one class per line
6. Place model and labels in `GameModels/{your_game}/`

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

## Support

- **GitHub**: [https://github.com/JPDoesDev/GamingVision](https://github.com/JPDoesDev/GamingVision)
- **Discord**: [https://discord.gg/xRn7hWZ52c](https://discord.gg/xRn7hWZ52c)
- **Youtube**: [https://www.youtube.com/@JPDoesDev](https://www.youtube.com/@JPDoesDev)
- **Issues**: [Report bugs or request features](https://github.com/JPDoesDev/GamingVision/issues)
- **Website**: [www.jpdoes.dev](https://www.jpdoes.dev)
- **Contact**: jpdoesdev@gmail.com

## Credits

- Built on the foundation of NoMansAccess Python project
- Uses [Ultralytics YOLO](https://github.com/ultralytics/ultralytics) for object detection
- Uses [ONNX Runtime](https://onnxruntime.ai/) for inference
