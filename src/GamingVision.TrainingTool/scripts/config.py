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
# Options: "yolo11n.pt" (nano/fast), "yolo11s.pt" (small), "yolo11m.pt" (medium)
BASE_MODEL = "yolo11n.pt"

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
#
# This configuration controls YOLO model training. Each setting is documented
# with its purpose, valid range, and recommended values for different scenarios.
#
# =============================================================================

TRAINING_CONFIG = {
    # =========================================================================
    # CORE TRAINING PARAMETERS
    # =========================================================================

    # DEVICE: Where to run training
    # Options: "cuda" (GPU), "cpu", or device ID (0, 1, etc.)
    # GPU is 10-50x faster than CPU. Use "cuda" for automatic GPU selection.
    "device": "cuda",

    # EPOCHS: Number of complete passes through the training data
    # Range: 50-600+
    # - 100-150: Quick experiments, small datasets
    # - 200-300: Standard training, good results
    # - 300-600: Maximum accuracy, large datasets
    # Watch validation metrics - stop early if val_loss stops improving
    "epochs": 250,

    # IMGSZ: Training image resolution (square)
    # Range: 320-1920
    # - 640: Standard, fast training (~30ms inference)
    # - 1280: Better for small objects
    # - 1440: Best for UI text detection, slower (~80ms inference)
    # Higher = better detail but slower training and inference
    "imgsz": 1440,

    # BATCH: Images processed per training step
    # Options:
    #   - Integer (4, 8, 16, 32): Fixed batch size
    #   - Float 0-1 (0.70): Use X% of GPU memory (auto-calculates batch)
    #   - -1: Auto-detect optimal batch size
    # Larger batches = more stable gradients but need more VRAM
    # At 1440px: batch=4-8 for 12GB GPU, batch=8-16 for 24GB GPU
    "batch": 0.70,

    # PATIENCE: Early stopping - stop if no improvement for N epochs
    # Range: 10-100
    # - 20-30: Stop quickly if stuck
    # - 50: Standard
    # - 100: Let it train longer before giving up
    "patience": 50,

    # =========================================================================
    # OPTIMIZER & LEARNING RATE
    # =========================================================================

    # LR0: Initial learning rate
    # Range: 0.0001 - 0.1
    # - SGD optimizer: 0.01 (default)
    # - Adam/AdamW: 0.001 (10x lower)
    # Too high = unstable training, too low = slow convergence
    # If training loss oscillates wildly, reduce lr0
    "lr0": 0.01,

    # WEIGHT_DECAY: L2 regularization to prevent overfitting
    # Range: 0.0001 - 0.001
    # - 0.0005: Standard (default)
    # - 0.001: More regularization if overfitting
    # Higher values = stronger regularization, may underfit
    "weight_decay": 0.0005,

    # WARMUP_EPOCHS: Gradual LR increase at start of training
    # Range: 0-10
    # - 3: Standard (default)
    # - 0: No warmup (can cause instability)
    # Helps prevent early gradient explosions
    "warmup_epochs": 3,

    # =========================================================================
    # DATA AUGMENTATION - COLOR
    # =========================================================================
    #
    # Color augmentation simulates different lighting/display conditions.
    # For UI detection: use CONSERVATIVE values since UI colors are consistent.
    # For real-world objects: use HIGHER values for robustness.
    #
    # =========================================================================

    # AUGMENT: Master switch for all augmentation
    "augment": True,

    # HSV_H: Hue shift (color wheel rotation)
    # Range: 0.0 - 0.5 (fraction of 360 degrees)
    # - 0.0: Disabled - colors stay exact (UI detection)
    # - 0.015: Very subtle shifts (default)
    # - 0.1: Moderate - red can shift toward orange/purple
    # For UI: keep at 0.0-0.015, UI colors are intentional
    # For real-world: 0.015-0.1 for lighting variation
    "hsv_h": 0.015,

    # HSV_S: Saturation adjustment (color intensity)
    # Range: 0.0 - 1.0
    # - 0.0: Disabled
    # - 0.3: Subtle variation
    # - 0.7: Aggressive - vivid colors can become muted (default)
    # For UI: 0.2-0.4 recommended, UI has consistent saturation
    # For real-world: 0.5-0.7 for different camera/lighting
    "hsv_s": 0.7,

    # HSV_V: Value/brightness adjustment
    # Range: 0.0 - 1.0
    # - 0.0: Disabled
    # - 0.2: Subtle brightness variation
    # - 0.4: Moderate (default)
    # For UI: 0.2-0.3, simulates monitor brightness differences
    # For real-world: 0.3-0.5 for shadow/highlight variation
    "hsv_v": 0.4,

    # =========================================================================
    # DATA AUGMENTATION - GEOMETRIC
    # =========================================================================
    #
    # Geometric augmentation changes position/orientation of objects.
    # For UI: DISABLE rotation/flip since UI is always upright and readable.
    # For real-world: Enable based on how objects appear in deployment.
    #
    # =========================================================================

    # DEGREES: Random rotation range
    # Range: 0.0 - 180.0 (degrees in either direction)
    # - 0.0: Disabled (UI detection - text must be readable)
    # - 10-15: Slight tilt (handheld camera)
    # - 45+: Objects that appear at any angle
    # For UI: ALWAYS 0.0 - rotated text is unreadable
    "degrees": 0.0,

    # TRANSLATE: Random position shift
    # Range: 0.0 - 0.5 (fraction of image size)
    # - 0.0: Disabled
    # - 0.1: 10% shift - objects move slightly (default)
    # - 0.2: 20% shift - objects can appear near edges
    # For UI: 0.1-0.2, UI elements appear in different screen positions
    "translate": 0.1,

    # SCALE: Random size scaling
    # Range: 0.0 - 0.9 (max scale factor variation)
    # - 0.0: Disabled - objects stay original size
    # - 0.3: ±30% size variation
    # - 0.5: ±50% size variation (default)
    # For UI: 0.2-0.3 recommended, UI has fixed sizes at given resolution
    # For real-world: 0.4-0.5 for distance variation
    "scale": 0.5,

    # FLIPLR: Horizontal flip probability
    # Range: 0.0 - 1.0
    # - 0.0: Disabled (UI - text would be backwards)
    # - 0.5: 50% chance to flip (symmetric objects)
    # For UI: ALWAYS 0.0 - mirrored text is unreadable
    # For real-world: 0.5 if objects are horizontally symmetric
    "fliplr": 0.0,

    # FLIPUD: Vertical flip probability
    # Range: 0.0 - 1.0
    # - 0.0: Disabled (most cases)
    # - 0.5: 50% chance to flip (aerial/satellite imagery)
    # For UI: ALWAYS 0.0 - upside-down UI makes no sense
    "flipud": 0.0,

    # =========================================================================
    # DATA AUGMENTATION - ADVANCED
    # =========================================================================

    # MOSAIC: Combine 4 images into one training image
    # Range: 0.0 - 1.0 (probability)
    # - 0.0: Disabled
    # - 1.0: Always enabled (default)
    # Pros: Increases effective batch diversity, helps small objects
    # Cons: At high imgsz (1440), creates 720px tiles - may lose detail
    # For UI at 1440px: Consider 0.5 or lower to preserve text clarity
    # For UI at 640px: 1.0 is fine
    "mosaic": 1.0,

    # MIXUP: Blend two images together
    # Range: 0.0 - 1.0 (probability)
    # - 0.0: Disabled (default, recommended for UI)
    # - 0.1-0.3: Subtle blending for real-world objects
    # Generally not useful for UI detection
    "mixup": 0.0,

    # CLOSE_MOSAIC: Disable mosaic for last N epochs
    # Range: 0 - epochs
    # - 10: Disable mosaic for final 10 epochs (default)
    # Allows model to fine-tune on full-resolution images at the end
    "close_mosaic": 10,

    # =========================================================================
    # PERFORMANCE SETTINGS
    # =========================================================================

    # WORKERS: Data loader threads
    # Range: 0 - 16
    # - 0: Load in main thread (debugging)
    # - 4-8: Standard
    # - 8-16: Fast storage (NVMe SSD)
    # More workers = faster data loading, but uses more RAM
    "workers": 8,

    # CACHE: Cache images in memory for faster training
    # Options: True, False, "ram", "disk"
    # - True/"ram": Cache in RAM (fastest, needs lots of RAM)
    # - "disk": Cache on disk (fast, needs disk space)
    # - False: No caching (slower, minimal memory)
    # For 500 images at 1440px: ~20GB RAM needed for full cache
    "cache": True,

    # AMP: Automatic Mixed Precision (FP16 training)
    # Options: True, False
    # - True: Use FP16 where possible (faster, less VRAM)
    # - False: Full FP32 precision
    # Recommended: True for modern GPUs (RTX 20xx+)
    "amp": True,

    # COS_LR: Use cosine learning rate schedule
    # Options: True, False
    # - True: LR decreases following cosine curve (smoother)
    # - False: Linear LR decrease
    # Cosine generally gives better results
    "cos_lr": True,
}

# =============================================================================
# EXPORT SETTINGS
# =============================================================================

# By default, export_model.py exports TWO models:
#   - 640x640:  Fast inference (~20-30ms) - good for real-time overlay at 30 FPS
#   - 1440x1440: High accuracy - better for small UI elements, but slower (~80-100ms)
#
# The 640 model is set as default. Users can switch by editing game_config.json.
# To export only one size: py -3.10 export_model.py --size 640

EXPORT_CONFIG = {
    "format": "onnx",
    "opset": 12,
    "simplify": True,
    "dynamic": False,
    # Note: imgsz is now handled by export_model.py (exports both 640 and 1440)
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
