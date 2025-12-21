"""
Shared configuration for GamingVision training scripts.
Update GAME_ID before running any scripts.
"""

import os
from pathlib import Path

# =============================================================================
# CONFIGURATION - UPDATE THESE VALUES
# =============================================================================

# The game you're training a model for (e.g., "arc_raiders", "no_mans_sky")
GAME_ID = "arc_raiders"

# Model name for the output files (e.g., "ArcRaidersModel", "NoMansModel")
MODEL_NAME = "ArcRaidersModel"

# Base YOLO model to use for training
# Options: "yolov8n.pt" (nano/fast), "yolov8s.pt" (small), "yolov8m.pt" (medium)
BASE_MODEL = "yolov8n.pt"

# =============================================================================
# PATHS - Auto-calculated, typically don't need to change
# =============================================================================

# Get the script directory and project root
SCRIPT_DIR = Path(__file__).parent.resolve()
TRAINING_TOOL_DIR = SCRIPT_DIR.parent
PROJECT_ROOT = TRAINING_TOOL_DIR.parent.parent

# Training data paths
TRAINING_DATA_DIR = TRAINING_TOOL_DIR / "training_data" / GAME_ID
IMAGES_DIR = TRAINING_DATA_DIR / "images"
LABELS_DIR = TRAINING_DATA_DIR / "labels"
CLASSES_FILE = TRAINING_DATA_DIR / "classes.txt"
DATASET_YAML = TRAINING_DATA_DIR / "dataset.yaml"

# Split dataset paths (created by split_dataset.py)
TRAIN_IMAGES_DIR = TRAINING_DATA_DIR / "train" / "images"
TRAIN_LABELS_DIR = TRAINING_DATA_DIR / "train" / "labels"
VAL_IMAGES_DIR = TRAINING_DATA_DIR / "val" / "images"
VAL_LABELS_DIR = TRAINING_DATA_DIR / "val" / "labels"

# Output paths
RUNS_DIR = SCRIPT_DIR / "runs" / "detect"
GAME_MODELS_DIR = PROJECT_ROOT / "GameModels" / GAME_ID

# =============================================================================
# TRAINING SETTINGS
# =============================================================================

TRAINING_CONFIG = {
    # Device for training: 'cuda' for GPU, 'cpu' for CPU, or 0 for first GPU
    # Use 'cuda' to force GPU training (recommended)
    "device": "cuda",

    # Number of training epochs (more = better but slower)
    # Recommended: 100-300 for good results
    "epochs": 150,

    # Image size for training (larger = more detail but slower)
    # 1440 recommended for UI/text detection, 640 for faster training
    "imgsz": 1440,

    # Batch size: float 0-1 = fraction of GPU memory, int = fixed batch size
    # 0.70 = use 70% of GPU memory (recommended for GPU training)
    # -1 = auto-detect based on GPU memory
    "batch": 0.70,

    # Patience for early stopping (stop if no improvement for N epochs)
    "patience": 50,

    # Learning rate (default is usually fine)
    "lr0": 0.01,

    # Weight decay for regularization
    "weight_decay": 0.0005,

    # Warmup epochs (gradual learning rate increase)
    "warmup_epochs": 3,

    # Data augmentation settings
    "augment": True,
    "hsv_h": 0.015,  # Hue augmentation
    "hsv_s": 0.7,    # Saturation augmentation
    "hsv_v": 0.4,    # Value/brightness augmentation
    "degrees": 0.0,  # Rotation (0 for UI elements - they're usually axis-aligned)
    "translate": 0.1,  # Translation
    "scale": 0.5,    # Scale augmentation
    "fliplr": 0.0,   # Horizontal flip (0 for UI - text would be backwards)
    "flipud": 0.0,   # Vertical flip (0 for UI)
    "mosaic": 1.0,   # Mosaic augmentation
    "mixup": 0.0,    # Mixup augmentation

    # Other settings
    "workers": 8,     # Data loader workers
    "cache": True,    # Cache images for faster training
    "amp": True,      # Automatic mixed precision (faster on modern GPUs)
    "cos_lr": True,   # Cosine learning rate scheduler
    "close_mosaic": 10,  # Disable mosaic for last N epochs
}

# =============================================================================
# EXPORT SETTINGS
# =============================================================================

EXPORT_CONFIG = {
    "format": "onnx",
    "opset": 12,
    "simplify": True,
    "dynamic": False,
    "imgsz": 1440,  # Should match training imgsz
}

# =============================================================================
# HELPER FUNCTIONS
# =============================================================================

def get_class_names() -> list[str]:
    """Read class names from classes.txt file."""
    if not CLASSES_FILE.exists():
        raise FileNotFoundError(f"Classes file not found: {CLASSES_FILE}")

    with open(CLASSES_FILE, 'r') as f:
        return [line.strip() for line in f if line.strip()]


def validate_paths():
    """Validate that required paths exist."""
    errors = []

    if not TRAINING_DATA_DIR.exists():
        errors.append(f"Training data directory not found: {TRAINING_DATA_DIR}")

    if not IMAGES_DIR.exists():
        errors.append(f"Images directory not found: {IMAGES_DIR}")

    if not CLASSES_FILE.exists():
        errors.append(f"Classes file not found: {CLASSES_FILE}")

    # Check for images
    if IMAGES_DIR.exists():
        images = list(IMAGES_DIR.glob("*.jpg")) + list(IMAGES_DIR.glob("*.png"))
        if len(images) == 0:
            errors.append(f"No images found in: {IMAGES_DIR}")
        else:
            print(f"Found {len(images)} images")

    # Check for labels
    if LABELS_DIR.exists():
        labels = list(LABELS_DIR.glob("*.txt"))
        print(f"Found {len(labels)} label files")

    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        return False

    return True


def print_config():
    """Print current configuration."""
    print("=" * 60)
    print("GamingVision Training Configuration")
    print("=" * 60)
    print(f"Game ID:        {GAME_ID}")
    print(f"Model Name:     {MODEL_NAME}")
    print(f"Base Model:     {BASE_MODEL}")
    print("-" * 60)
    print(f"Training Data:  {TRAINING_DATA_DIR}")
    print(f"Output Dir:     {GAME_MODELS_DIR}")
    print("-" * 60)
    print(f"Device:         {TRAINING_CONFIG['device']}")
    print(f"Epochs:         {TRAINING_CONFIG['epochs']}")
    print(f"Image Size:     {TRAINING_CONFIG['imgsz']}")
    batch = TRAINING_CONFIG['batch']
    if batch == -1:
        print(f"Batch Size:     auto")
    elif isinstance(batch, float) and 0 < batch < 1:
        print(f"Batch Size:     {batch:.0%} of GPU memory")
    else:
        print(f"Batch Size:     {batch}")
    print("=" * 60)


if __name__ == "__main__":
    print_config()
    print()
    print("Validating paths...")
    if validate_paths():
        print("All paths valid!")
        print()
        print("Class names:")
        for i, name in enumerate(get_class_names()):
            print(f"  {i}: {name}")
