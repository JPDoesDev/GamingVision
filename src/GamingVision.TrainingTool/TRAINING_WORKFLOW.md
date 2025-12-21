# GamingVision Model Training Workflow

This guide covers the complete workflow for creating a custom YOLO model for a new game.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Step 1: Create Game Profile](#step-1-create-game-profile)
3. [Step 2: Collect Training Data](#step-2-collect-training-data)
4. [Step 3: Label Images with LabelImg](#step-3-label-images-with-labelimg)
5. [Step 4: Prepare Dataset for Training](#step-4-prepare-dataset-for-training)
6. [Step 5: Train YOLO Model](#step-5-train-yolo-model)
7. [Step 6: Export to ONNX](#step-6-export-to-onnx)
8. [Step 7: Integrate with GamingVision](#step-7-integrate-with-gamingvision)
9. [Tips and Best Practices](#tips-and-best-practices)

---

## Prerequisites

### Python Environment

Install Python 3.10+ and the required packages:

```powershell
# Navigate to scripts folder
cd D:\Dev\GamingVision\src\GamingVision.TrainingTool\scripts

# Install PyTorch with CUDA 12.6 first
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu126

# Install remaining dependencies from requirements.txt
py -3.10 -m pip install -r requirements.txt

# Verify installations
py -3.10 -c "import labelImg; print('LabelImg OK')"
py -3.10 -c "from ultralytics import YOLO; print('Ultralytics OK')"
```

### Directory Structure

After setup, you'll have:

```
GamingVision/
├── GameModels/
│   └── {game_id}/
│       ├── game_config.json      # Game settings
│       ├── {ModelName}.onnx      # Trained model (after training)
│       └── {ModelName}.txt       # Label names
│
└── src/GamingVision.TrainingTool/
    ├── scripts/                  # Python training scripts
    │   ├── config.py             # Configuration (edit GAME_ID here)
    │   ├── split_dataset.py      # Split into train/val
    │   ├── train_model.py        # Train YOLO model
    │   └── export_model.py       # Export ONNX & deploy
    │
    └── training_data/
        └── {game_id}/
            ├── images/           # Screenshots
            ├── labels/           # YOLO annotations
            └── classes.txt       # Label definitions
```

---

## Step 1: Create Game Profile

### Option A: Using Training Tool (Recommended)

```powershell
cd D:\Dev\GamingVision
dotnet run --project src\GamingVision.TrainingTool
```

Select **"Create New Game Profile"** and follow the prompts:
1. Enter a game ID (e.g., `arc_raiders`) - lowercase, underscores only
2. Enter display name (e.g., `ARC Raiders`)
3. Enter window title for capture (e.g., `arc_raiders.exe`)

This creates:
- `GameModels/{game_id}/game_config.json` - Configuration file
- `training_data/{game_id}/` - Training data folders

### Option B: Manual Setup

1. **Create the GameModels folder:**

```powershell
mkdir GameModels\{game_id}
```

2. **Create `game_config.json`:**

```json
{
  "gameId": "your_game_id",
  "displayName": "Your Game Name",
  "modelFile": "YourGameModel.onnx",
  "windowTitle": "game.exe",
  "primaryLabels": [],
  "secondaryLabels": [],
  "tertiaryLabels": [],
  "labelPriority": [],
  "hotkeys": {
    "readPrimary": "Alt+1",
    "readSecondary": "Alt+2",
    "readTertiary": "Alt+3",
    "stopReading": "Alt+4",
    "toggleDetection": "Alt+5",
    "quit": "Alt+Q"
  },
  "capture": {
    "method": "window",
    "monitorIndex": 0,
    "captureCursor": false,
    "scaleFactor": 1,
    "regionOfInterest": ""
  },
  "tts": {
    "engine": "sapi",
    "primaryVoice": "Microsoft David",
    "primaryRate": 3,
    "secondaryVoice": "Microsoft David",
    "secondaryRate": 0,
    "tertiaryVoice": "Microsoft David",
    "tertiaryRate": 0,
    "volume": 100
  },
  "detection": {
    "autoReadCooldown": 2000,
    "confidenceThreshold": 0.3,
    "autoReadConfidenceThreshold": 0.6,
    "nmsThreshold": 0.45,
    "maxDetections": 100,
    "targetFps": 10,
    "autoReadEnabled": false,
    "onlyReadChanges": true,
    "readPrimaryLabelAloud": true,
    "readSecondaryLabelAloud": false,
    "readTertiaryLabelAloud": false
  }
}
```

3. **Create training data folders:**

```powershell
mkdir src\GamingVision.TrainingTool\training_data\{game_id}\images
mkdir src\GamingVision.TrainingTool\training_data\{game_id}\labels
```

---

## Step 2: Collect Training Data

### Using the Training Tool

```powershell
dotnet run --project src\GamingVision.TrainingTool
```

1. Select your game from the list
2. Start the game and navigate to screens you want to capture
3. Press **F1** to capture a screenshot
4. Press **Escape** to return to the menu

**Capture Tips:**
- Capture diverse scenarios (menus, inventory, HUD, dialog boxes)
- Capture different states (full inventory, empty inventory, etc.)
- Aim for 100-500+ images for a good model
- The tool saves to `training_data/{game_id}/images/`

### Manual Screenshot Collection

Alternatively, take screenshots manually and save them as:
- Format: JPEG or PNG
- Location: `training_data/{game_id}/images/`
- Naming: Any consistent naming (e.g., `screenshot_0001.jpg`)

---

## Step 3: Label Images with LabelImg

### Define Your Classes

Before labeling, decide what UI elements to detect. Create/edit `classes.txt`:

```
training_data/{game_id}/classes.txt
```

Example for an inventory system:
```
inventory-title
item-slot
item-name
item-description
item-quantity
currency-display
close-button
```

**Naming Tips:**
- Use lowercase with hyphens
- Be specific (e.g., `health-bar` not just `bar`)
- Group related items (e.g., `inv-title`, `inv-item`, `inv-slot`)

### Launch LabelImg

```powershell
# Replace {game_id} with your actual game ID (e.g., arc_raiders)
py -3.10 -m labelImg.labelImg "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\{game_id}\images" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\{game_id}\classes.txt" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\{game_id}\labels"
```

**Example for arc_raiders:**
```powershell
py -3.10 -m labelImg.labelImg "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\arc_raiders\images" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\arc_raiders\classes.txt" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\arc_raiders\labels"
```

### LabelImg Settings

1. **Set format to YOLO:**
   - Click "PascalVOC" button (bottom left) until it shows "YOLO"

2. **Enable auto-save:**
   - View > Auto Save Mode

### Labeling Workflow

| Shortcut | Action |
|----------|--------|
| `W` | Create rectangle (bounding box) |
| `D` | Next image |
| `A` | Previous image |
| `Ctrl+S` | Save |
| `Del` | Delete selected box |

**Labeling Tips:**
- Draw tight bounding boxes around UI elements
- Label ALL instances of each class in every image
- Be consistent with what you label
- Skip images that don't contain relevant UI elements

### Verify Labels

After labeling, check that `.txt` files exist in the `labels/` folder:

```powershell
ls src\GamingVision.TrainingTool\training_data\{game_id}\labels\
```

Each `.txt` file should contain lines like:
```
0 0.512500 0.341667 0.125000 0.083333
1 0.256250 0.508333 0.087500 0.041667
```

Format: `class_id center_x center_y width height` (normalized 0-1)

---

## Step 4: Prepare Dataset for Training

### Configure Your Game

Before running the scripts, edit `scripts/config.py` to set your game:

```python
# The game you're training a model for
GAME_ID = "arc_raiders"

# Model name for the output files
MODEL_NAME = "ArcRaidersModel"

# Base YOLO model (n=fast, s=balanced, m=accurate)
BASE_MODEL = "yolov8n.pt"
```

### Split Dataset

Run the split script to create train/val sets and generate `dataset.yaml`:

```powershell
cd D:\Dev\GamingVision\src\GamingVision.TrainingTool\scripts
py -3.10 split_dataset.py
```

This script:
- Finds all labeled images (images with non-empty label files)
- Splits them 80% train / 20% validation
- Copies files to `train/` and `val/` subfolders
- Creates `dataset.yaml` for YOLO training

**Output:**
```
training_data/{game_id}/
├── train/
│   ├── images/
│   └── labels/
├── val/
│   ├── images/
│   └── labels/
└── dataset.yaml
```

---

## Step 5: Train YOLO Model

### Run Training Script

```powershell
cd D:\Dev\GamingVision\src\GamingVision.TrainingTool\scripts
py -3.10 train_model.py
```

The script uses optimized settings for UI detection:
- **150 epochs** (configurable in config.py)
- **Auto batch size** (adjusts to your GPU memory)
- **UI-optimized augmentation** (no flips - text would be backwards)
- **Cosine learning rate** scheduler
- **Early stopping** if no improvement for 50 epochs

### Custom Training Options

```powershell
# Train with custom epoch count
py -3.10 train_model.py --epochs 200

# Resume interrupted training
py -3.10 train_model.py --resume
```

### Training Parameters (in config.py)

| Parameter | Default | Description |
|-----------|---------|-------------|
| `epochs` | 150 | Training iterations |
| `imgsz` | 640 | Image size |
| `batch` | -1 (auto) | Batch size |
| `patience` | 50 | Early stopping patience |

### Monitor Training

Training outputs to `scripts/runs/detect/{game_id}_model/`:
- `weights/best.pt` - Best model weights
- `weights/last.pt` - Latest model weights
- `results.png` - Training metrics graphs
- `confusion_matrix.png` - Class prediction accuracy

### Resume Training

If training is interrupted:

```powershell
py -3.10 train_model.py --resume
```

---

## Step 6: Export to ONNX

### Run Export Script

```powershell
cd D:\Dev\GamingVision\src\GamingVision.TrainingTool\scripts
py -3.10 export_model.py
```

This script automatically:
1. Loads the best trained model (`best.pt`)
2. Exports to ONNX format (optimized for DirectML)
3. Copies `{ModelName}.onnx` to `GameModels/{game_id}/`
4. Creates `{ModelName}.txt` label file
5. Updates `modelFile` in `game_config.json`

### Export Options

```powershell
# Use last.pt instead of best.pt
py -3.10 export_model.py --use-last

# Export only, don't copy to GameModels
py -3.10 export_model.py --no-deploy
```

### Output Files

After export, your GameModels folder will contain:

```
GameModels/{game_id}/
├── game_config.json      # Updated with modelFile
├── {ModelName}.onnx      # Trained model (~12-50 MB)
└── {ModelName}.txt       # Label names (one per line)
```

---

## Step 7: Integrate with GamingVision

### Update game_config.json

Edit `GameModels/{game_id}/game_config.json`:

```json
{
  "gameId": "arc_raiders",
  "displayName": "ARC Raiders",
  "modelFile": "ArcRaidersModel.onnx",
  "windowTitle": "ARC Raiders",
  "primaryLabels": ["inventory-title", "item-name"],
  "secondaryLabels": ["item-slot", "item-quantity"],
  "tertiaryLabels": ["close-button"],
  "labelPriority": ["inventory-title", "item-name", "item-slot"],
  ...
}
```

### Label Tier Configuration

- **primaryLabels**: Read automatically when auto-read is enabled (Alt+1)
- **secondaryLabels**: Read on-demand only (Alt+2)
- **tertiaryLabels**: Read on-demand only (Alt+3)
- **labelPriority**: Order for auto-reading (highest priority first)

### Test the Model

1. Run GamingVision:
   ```powershell
   dotnet run -c Release --project src\GamingVision
   ```

2. Select your game from the dropdown
3. Start the game
4. Click "Start Detection" or press Alt+5
5. Press Alt+1/2/3 to read detected objects

---

## Tips and Best Practices

### Data Collection

- **Quantity**: 100-500 images minimum, more is better
- **Variety**: Different UI states, resolutions, lighting conditions
- **Consistency**: If you change game settings (resolution, UI scale), recollect data

### Labeling

- **Tight boxes**: Draw boxes as close to the element as possible
- **Complete labeling**: Label ALL instances of each class in every image
- **Skip irrelevant**: Don't force labels on images without your target elements

### Training

- **Start small**: Begin with `yolov8n.pt` for faster iteration
- **Watch metrics**: If validation loss increases while training loss decreases, you're overfitting
- **Augmentation**: YOLO applies augmentation automatically (flips, scaling, etc.)

### Model Selection

| Model | Speed | Accuracy | Use Case |
|-------|-------|----------|----------|
| YOLOv8n | Fastest | Good | Real-time, limited GPU |
| YOLOv8s | Fast | Better | Balanced |
| YOLOv8m | Medium | Best | High accuracy needed |

### Troubleshooting

**Low accuracy:**
- Add more training images
- Check for labeling errors
- Increase epochs
- Try a larger model (n → s → m)

**Slow inference:**
- Use smaller model (m → s → n)
- Reduce `targetFps` in game_config.json
- Enable GPU (check `useDirectML` in app_settings.json)

**Model not loading:**
- Verify ONNX file path in game_config.json
- Check that .txt labels file exists with same base name
- Ensure class count matches between model and labels file

---

## Quick Reference

### File Locations

| File | Purpose |
|------|---------|
| `GameModels/{game}/game_config.json` | Game settings |
| `GameModels/{game}/{Name}Model.onnx` | Trained model |
| `GameModels/{game}/{Name}Model.txt` | Label names |
| `scripts/config.py` | Training configuration |
| `scripts/runs/detect/{game}_model/` | Training output |
| `training_data/{game}/images/` | Screenshots |
| `training_data/{game}/labels/` | YOLO annotations |
| `training_data/{game}/classes.txt` | Class definitions |
| `training_data/{game}/dataset.yaml` | Dataset config (auto-generated) |

### Commands Summary

```powershell
# 1. Collect screenshots
dotnet run --project src\GamingVision.TrainingTool

# 2. Label images (replace {game_id} with your game)
py -3.10 -m labelImg.labelImg "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\{game_id}\images" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\{game_id}\classes.txt" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\{game_id}\labels"

# 3. Configure (edit GAME_ID and MODEL_NAME)
notepad src\GamingVision.TrainingTool\scripts\config.py

# 4. Split dataset
cd src\GamingVision.TrainingTool\scripts
py -3.10 split_dataset.py

# 5. Train model
py -3.10 train_model.py

# 6. Export and deploy
py -3.10 export_model.py

# 7. Run GamingVision
cd D:\Dev\GamingVision
dotnet run -c Release --project src\GamingVision
```

### Script Quick Reference

| Script | Purpose | When to Use |
|--------|---------|-------------|
| `split_dataset.py` | Create train/val split | After labeling images |
| `train_model.py` | Train YOLO model | After splitting dataset |
| `train_model.py --resume` | Continue training | If training was interrupted |
| `export_model.py` | Export & deploy model | After training completes |

---

*Last Updated: 2025-12-17*
