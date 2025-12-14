# GameModels Directory

This directory contains per-game YOLO models and configuration files for GamingVision.

## Directory Structure

```
GameModels/
├── README.md                    # This file
├── {game_id}/                   # One folder per supported game
│   ├── README.md                # Game-specific documentation
│   ├── {ModelName}.onnx         # YOLO model in ONNX format
│   ├── {ModelName}.txt          # Class labels (one per line, order matches model)
│   └── game_config.json         # Game profile settings
```

## Adding a New Game

1. **Create a game folder** with a unique ID (e.g., `my_game`)

2. **Add the ONNX model** - Export your trained YOLO model to ONNX format:
   ```python
   from ultralytics import YOLO
   model = YOLO('your_model.pt')
   model.export(format='onnx', imgsz=640, simplify=True)
   ```

3. **Create the labels file** (`{ModelName}.txt`) with one class label per line.
   The order must match the class indices in your trained model.

4. **Create `game_config.json`** with your game settings:
   ```json
   {
     "gameId": "my_game",
     "displayName": "My Game",
     "modelFile": "MyModel.onnx",
     "windowTitle": "My Game Window Title",
     "primaryLabels": ["label1", "label2"],
     "secondaryLabels": ["label3", "label4"],
     ...
   }
   ```

## Extracting Labels from Existing Models

### From ONNX Model (recommended)
```python
import onnx

model = onnx.load('YourModel.onnx')
for prop in model.metadata_props:
    if prop.key == 'names':
        print(f"Classes: {prop.value}")
```

### From PyTorch (.pt) Model
```python
from ultralytics import YOLO

model = YOLO('YourModel.pt')
print(model.names)  # Dict of {index: label_name}
```

## Configuration Reference

### game_config.json Fields

| Field | Description |
|-------|-------------|
| `gameId` | Unique identifier for the game |
| `displayName` | Human-readable name shown in UI |
| `modelFile` | ONNX model filename |
| `windowTitle` | Window title to capture (for window mode) |
| `primaryLabels` | Labels that trigger auto-read |
| `secondaryLabels` | Labels read only on manual request |
| `labelPriority` | Order of importance for reading multiple detections |
| `hotkeys` | Key bindings for this game profile |
| `capture` | Screen capture settings |
| `tts` | Text-to-speech voice settings |
| `detection` | Confidence thresholds and timing settings |

### Label File Format

The `.txt` label file must have one label per line, with line order matching the model's class indices:

```
class_0_name
class_1_name
class_2_name
...
```

**Important:** Labels are case-sensitive and must exactly match what you use in `primaryLabels` and `secondaryLabels`.
