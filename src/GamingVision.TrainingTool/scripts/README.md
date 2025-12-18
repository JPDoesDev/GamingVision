# GamingVision Training Scripts

Python scripts for training YOLO models for GamingVision.

## Quick Start

```powershell
cd D:\Dev\GamingVision\src\GamingVision.TrainingTool\scripts

# 1. Install dependencies
py -3.9 -m pip install -r requirements.txt

# 2. Configure for your game (edit GAME_ID and MODEL_NAME)
notepad config.py

# 3. Split dataset into train/val
py -3.9 split_dataset.py

# 4. Train the model
py -3.9 train_model.py

# 5. Export and deploy
py -3.9 export_model.py
```

## Scripts

| Script | Purpose |
|--------|---------|
| `config.py` | Shared configuration - **edit this first!** |
| `split_dataset.py` | Split labeled images into train/val sets |
| `train_model.py` | Train YOLO model on your dataset |
| `export_model.py` | Export to ONNX and copy to GameModels |

## Prerequisites

Before running these scripts:

1. **Collect screenshots** using the Training Tool
2. **Label images** with LabelImg
3. **Create classes.txt** with your label names

See `TRAINING_WORKFLOW.md` in the parent folder for the complete workflow.

## Configuration

Edit `config.py` before running:

```python
# The game you're training for
GAME_ID = "arc_raiders"

# Output model name
MODEL_NAME = "ArcRaidersModel"

# Base model (n=fast, s=balanced, m=accurate)
BASE_MODEL = "yolov8n.pt"
```

## Training Tips

- **Start with yolov8n.pt** for faster iteration
- **100+ labeled images** recommended
- **150 epochs** is a good starting point
- Watch for overfitting (val loss increasing while train loss decreases)

## Output

After training and export:

```
GameModels/{game_id}/
├── {ModelName}.onnx    # Trained model
├── {ModelName}.txt     # Label names
└── game_config.json    # Game configuration
```
