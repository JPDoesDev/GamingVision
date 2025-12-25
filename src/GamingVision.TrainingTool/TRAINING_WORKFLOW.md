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

### Step 1: Capture Screenshots

```powershell
dotnet run --project src\GamingVision.TrainingTool
```

1. Select your game (or create new profile)
2. Start the game
3. Press **F1** to capture screenshots
4. Press **Escape** when done

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
| Capture | `dotnet run --project src\GamingVision.TrainingTool` |
| Label | `py -3.10 -m labelImg.labelImg ...` |
| Train | `py -3.10 01_train_pipeline.py` |
| Test | `dotnet run -c Release --project src\GamingVision` |

---

*Last Updated: 2025-12-24*
