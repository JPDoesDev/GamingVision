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

2. Launch LabelImg:
   ```powershell
   py -3.10 -m labelImg.labelImg "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\{game_id}\images" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\{game_id}\classes.txt" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\{game_id}\labels"
   ```

   **Example for arc_raiders:**
   ```powershell
   py -3.10 -m labelImg.labelImg "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\arc_raiders\images" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\arc_raiders\classes.txt" "D:\Dev\GamingVision\src\GamingVision.TrainingTool\training_data\arc_raiders\labels"
   ```

3. Label all UI elements:
   - Press **W** to draw bounding box
   - Press **D** for next image
   - Set format to **YOLO** (bottom left button)

---

### Step 3: Configure & Train

1. Edit `scripts/config.py`:
   ```python
   GAME_ID = "your_game_id"
   MODEL_NAME = "YourGameModel"
   ```

2. Run the training pipeline:
   ```powershell
   cd src\GamingVision.TrainingTool\scripts
   py -3.10 01_train_pipeline.py
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
| Capture | `dotnet run -c Release --project src\GamingVision` (Training tab) |
| Label | `py -3.10 -m labelImg.labelImg ...` |
| Train | `py -3.10 01_train_pipeline.py` |
| Test | `dotnet run -c Release --project src\GamingVision` |

---

*Last Updated: 2026-01-08*
