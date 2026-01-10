# GamingVision Model Training Workflow

> **Note:** This is a work in progress. If you want to add or modify models, contact [jpdoes.dev](https://www.jpdoes.dev) for assistance.

Simple guide to create a custom YOLO model for a new game.

---

## Prerequisites

**Required:**
- Python 3.10
- CUDA 13.0
- PyTorch matching your CUDA version

> **Warning:** The combination of Python, CUDA, and PyTorch versions is very fragile. Getting them to all agree is most of the battle. Visit [pytorch.org](https://pytorch.org/) to find the correct install command for your setup.

```powershell
cd src\GamingVision.TrainingTool\scripts

# Install PyTorch with CUDA 13.0 (must match your CUDA version!)
pip3 install torch torchvision --index-url https://download.pytorch.org/whl/cu130

# Install remaining dependencies
pip install -r requirements.txt
```

---

## Workflow

### Step 1: Create Game Profile & Capture Screenshots

```powershell
dotnet run -c Release --project src\GamingVision
```

1. Go to the **Training** tab
2. Click **CREATE NEW** to create a new game profile
3. Fill in:
   - **Display Name**: Human-readable name (e.g., "Arc Raiders")
   - **Game ID**: Auto-generated folder name (e.g., "arc_raiders")
   - **Window Title**: Select from open windows or type manually
   - **Capture Method**: Window (recommended) or Fullscreen
4. Click **Create**
5. Enable the **"Enable screenshots for training"** checkbox
6. (Optional) Change the capture hotkey in **Capture Settings** (default: F1)
7. Start your game
8. Press the capture hotkey (default **F1**) to capture screenshots

> **Note:** The detection engine does NOT need to be running to capture screenshots. Just enable the training checkbox and press the capture hotkey.

Aim for 100-500+ images covering different UI states.

---

### Step 2: Label Images

1. Define your classes in `training_data/{game_id}/classes.txt`:
   ```
   inventory-title
   item-slot
   item-name
   health-bar
   ```

2. Launch mlabelImg (recommended via GUI):
   - In the main app, click **TRAIN** button
   - Click **LAUNCH mlabelImg** button

   Or manually from command line:
   ```powershell
   mlabelImg "training_data\{game_id}\images" "training_data\{game_id}\classes.txt" "training_data\{game_id}\labels"
   ```

3. Label all UI elements:
   - Press **W** to draw bounding box
   - Press **D** for next image
   - Set format to **YOLO** (bottom left button)

---

### Step 3: Train via GUI (Recommended)

1. In the main app, click the **TRAIN** button (next to POST PROCESS)
2. The Training Window opens:
   - **Training Data Paths**: Shows New and Base training data locations
   - **Training Mode**: Select Fine-tune or Full Retrain
   - **Launch mlabelImg**: Opens annotation tool directly
3. Click **START TRAINING**
4. If prerequisites are missing, a dialog shows what needs to be installed:
   - Python 3.10 - Download link to python.org
   - CUDA 13.0 - Download link to nvidia.com
   - PyTorch - Install button opens terminal
   - Packages - Install button opens terminal
5. The **Training Parameters** dialog appears:
   - **Epochs**: Number of training passes (default: 150)
   - **Image Size**: Training resolution (640/1280/1440)
   - **Batch**: GPU memory usage (0.5-0.9)
   - **Patience**: Early stopping patience
   - **Learning Rate**: Initial learning rate
   - **Device**: cuda or cpu
   - **Workers**: Data loader threads
   - **Cache/Mixed Precision**: Performance options
6. Click **Start Training** - a terminal window opens showing training progress
7. The terminal stays open when training completes so you can see the results

Parameters are saved per-game in `game_config.json` for next time.

### Step 3 (Alternative): Train via Command Line

1. Edit `scripts/config.py`:
   ```python
   GAME_ID = "your_game_id"
   MODEL_NAME = "YourGameModel"
   ```

2. Run the training pipeline:
   ```powershell
   cd src\GamingVision.TrainingTool\scripts
   python 01_train_pipeline.py
   ```

   Or with custom parameters:
   ```powershell
   python 01_train_pipeline.py --epochs 200 --imgsz 1440 --batch 0.70
   ```

The pipeline will:
- Split dataset into train/val
- Train the YOLO model
- Export to ONNX
- Deploy to `GameModels/{game_id}/`

---

### Step 4: Configure Labels

Edit `GameModels/{game_id}/game_config.json`:

```json
{
  "primaryLabels": ["inventory-title", "item-name"],
  "secondaryLabels": ["item-slot"],
  "tertiaryLabels": ["close-button"]
}
```

---

### Step 5: Test

```powershell
dotnet run -c Release --project src\GamingVision
```

Select your game and click **Start Engine**.

---

## Quick Reference

| Step | Command |
|------|---------|
| Capture | Main app > Training tab > Enable checkbox > Press F1 |
| Label | Main app > TRAIN button > Launch mlabelImg |
| Train (GUI) | Main app > TRAIN button > START TRAINING |
| Train (CLI) | `python 01_train_pipeline.py` |
| Test | Main app > Start Engine |

## Training Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| Epochs | 150 | Training passes (50-600) |
| Image Size | 1440 | Resolution (640/1280/1440) |
| Batch | 0.70 | GPU memory fraction (0.5-0.9) |
| Patience | 50 | Early stop epochs (10-100) |
| Learning Rate | 0.01 | Initial LR (0.001-0.1) |
| Device | cuda | Training device (cuda/cpu) |
| Workers | 8 | Data loader threads (0-16) |

---

*Last Updated: 2026-01-10*
