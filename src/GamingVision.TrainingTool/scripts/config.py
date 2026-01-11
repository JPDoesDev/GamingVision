"""
Shared configuration for GamingVision training scripts.

Configuration can be overridden via command-line arguments or by calling
apply_config_overrides() before using the config values.

Usage from command line:
    py -3.10 train_model.py --game-id arc_raiders --training-data-path "C:\\path"

Usage from code:
    from config import apply_config_overrides, get_config
    apply_config_overrides(game_id="arc_raiders", training_data_path="C:\\path")
    cfg = get_config()
"""

import os
from pathlib import Path
from dataclasses import dataclass, field
from typing import Optional

# =============================================================================
# DEFAULT CONFIGURATION VALUES
# =============================================================================

# The game you're training a model for (e.g., "arc_raiders", "no_mans_sky")
_DEFAULT_GAME_ID = "arc_raiders"

# Model name for the output files (e.g., "ArcRaidersModel", "NoMansModel")
_DEFAULT_MODEL_NAME = "ArcRaidersModel"

# Base YOLO model to use for training
# Options: "yolo11n.pt" (nano/fast), "yolo11s.pt" (small), "yolo11m.pt" (medium)
_DEFAULT_BASE_MODEL = "yolo11n.pt"

# =============================================================================
# RUNTIME CONFIGURATION (can be overridden)
# =============================================================================

@dataclass
class RuntimeConfig:
    """Runtime configuration that can be overridden via CLI or code."""
    game_id: str = _DEFAULT_GAME_ID
    model_name: str = _DEFAULT_MODEL_NAME
    base_model: str = _DEFAULT_BASE_MODEL
    training_data_path: Optional[str] = None  # Override TRAINING_DATA_DIR
    game_models_path: Optional[str] = None    # Override GAME_MODELS_DIR
    stats_output_path: Optional[str] = None   # Where to save training stats
    fine_tune_model_path: Optional[str] = None  # Path to best.pt for fine-tuning


# Global runtime config instance
_runtime_config = RuntimeConfig()


def apply_config_overrides(
    game_id: Optional[str] = None,
    model_name: Optional[str] = None,
    base_model: Optional[str] = None,
    training_data_path: Optional[str] = None,
    game_models_path: Optional[str] = None,
    stats_output_path: Optional[str] = None,
    fine_tune_model_path: Optional[str] = None,
):
    """Apply configuration overrides at runtime.

    Call this before using any config values to override defaults.
    """
    global _runtime_config, GAME_ID, MODEL_NAME, BASE_MODEL
    global TRAINING_DATA_DIR, IMAGES_DIR, LABELS_DIR, CLASSES_FILE, DATASET_YAML
    global TRAIN_IMAGES_DIR, TRAIN_LABELS_DIR, VAL_IMAGES_DIR, VAL_LABELS_DIR
    global GAME_MODELS_DIR

    if game_id is not None:
        _runtime_config.game_id = game_id
    if model_name is not None:
        _runtime_config.model_name = model_name
    if base_model is not None:
        _runtime_config.base_model = base_model
    if training_data_path is not None:
        _runtime_config.training_data_path = training_data_path
    if game_models_path is not None:
        _runtime_config.game_models_path = game_models_path
    if stats_output_path is not None:
        _runtime_config.stats_output_path = stats_output_path
    if fine_tune_model_path is not None:
        _runtime_config.fine_tune_model_path = fine_tune_model_path

    # Update module-level variables
    _recalculate_paths()


def get_runtime_config() -> RuntimeConfig:
    """Get the current runtime configuration."""
    return _runtime_config


def add_config_arguments(parser):
    """Add standard config arguments to an argparse parser.

    Usage:
        parser = argparse.ArgumentParser()
        add_config_arguments(parser)
        args = parser.parse_args()
        apply_args_to_config(args)
    """
    group = parser.add_argument_group("Configuration Overrides")
    group.add_argument("--game-id", type=str, help="Game identifier (e.g., arc_raiders)")
    group.add_argument("--model-name", type=str, help="Model name for output files")
    group.add_argument("--base-model", type=str, help="Base YOLO model (e.g., yolo11n.pt)")
    group.add_argument("--training-data-path", type=str, help="Path to training data directory")
    group.add_argument("--game-models-path", type=str, help="Path to GameModels output directory")
    group.add_argument("--stats-output-path", type=str, help="Path to save training statistics")
    group.add_argument("--fine-tune-model-path", type=str, help="Path to best.pt for fine-tuning")

    # Training parameters (override TRAINING_CONFIG values)
    train_group = parser.add_argument_group("Training Parameters")
    train_group.add_argument("--epochs", type=int, help="Number of training epochs (default: 150)")
    train_group.add_argument("--imgsz", type=int, help="Training image size (default: 1440)")
    train_group.add_argument("--batch", type=float, help="Batch size or GPU memory fraction (default: 0.70)")
    train_group.add_argument("--patience", type=int, help="Early stopping patience (default: 50)")
    train_group.add_argument("--lr0", type=float, help="Initial learning rate (default: 0.01)")
    train_group.add_argument("--device", type=str, help="Training device: cuda or cpu (default: cuda)")
    train_group.add_argument("--workers", type=int, help="Data loader workers (default: 8)")
    train_group.add_argument("--cache", action="store_true", dest="cache", help="Enable image caching")
    train_group.add_argument("--no-cache", action="store_false", dest="cache", help="Disable image caching")
    train_group.add_argument("--amp", action="store_true", dest="amp", help="Enable mixed precision (FP16)")
    train_group.add_argument("--no-amp", action="store_false", dest="amp", help="Disable mixed precision")
    parser.set_defaults(cache=None, amp=None)  # None means use TRAINING_CONFIG default


def apply_args_to_config(args):
    """Apply parsed arguments to configuration.

    Usage:
        args = parser.parse_args()
        apply_args_to_config(args)
    """
    apply_config_overrides(
        game_id=getattr(args, 'game_id', None),
        model_name=getattr(args, 'model_name', None),
        base_model=getattr(args, 'base_model', None),
        training_data_path=getattr(args, 'training_data_path', None),
        game_models_path=getattr(args, 'game_models_path', None),
        stats_output_path=getattr(args, 'stats_output_path', None),
        fine_tune_model_path=getattr(args, 'fine_tune_model_path', None),
    )

    # Apply training parameter overrides
    if getattr(args, 'epochs', None) is not None:
        TRAINING_CONFIG['epochs'] = args.epochs
    if getattr(args, 'imgsz', None) is not None:
        TRAINING_CONFIG['imgsz'] = args.imgsz
    if getattr(args, 'batch', None) is not None:
        TRAINING_CONFIG['batch'] = args.batch
    if getattr(args, 'patience', None) is not None:
        TRAINING_CONFIG['patience'] = args.patience
    if getattr(args, 'lr0', None) is not None:
        TRAINING_CONFIG['lr0'] = args.lr0
    if getattr(args, 'device', None) is not None:
        TRAINING_CONFIG['device'] = args.device
    if getattr(args, 'workers', None) is not None:
        TRAINING_CONFIG['workers'] = args.workers
    if getattr(args, 'cache', None) is not None:
        TRAINING_CONFIG['cache'] = args.cache
    if getattr(args, 'amp', None) is not None:
        TRAINING_CONFIG['amp'] = args.amp


# =============================================================================
# PATHS - Auto-calculated based on configuration
# =============================================================================

# Get the script directory and project root
SCRIPT_DIR = Path(__file__).parent.resolve()
TRAINING_TOOL_DIR = SCRIPT_DIR.parent
PROJECT_ROOT = TRAINING_TOOL_DIR.parent.parent


def _recalculate_paths():
    """Recalculate all path variables based on current runtime config."""
    global GAME_ID, MODEL_NAME, BASE_MODEL
    global TRAINING_DATA_DIR, IMAGES_DIR, LABELS_DIR, CLASSES_FILE, DATASET_YAML
    global TRAIN_IMAGES_DIR, TRAIN_LABELS_DIR, VAL_IMAGES_DIR, VAL_LABELS_DIR
    global GAME_MODELS_DIR

    cfg = _runtime_config

    # Update simple values
    GAME_ID = cfg.game_id
    MODEL_NAME = cfg.model_name
    BASE_MODEL = cfg.base_model

    # Calculate training data path
    if cfg.training_data_path:
        TRAINING_DATA_DIR = Path(cfg.training_data_path)
    else:
        TRAINING_DATA_DIR = TRAINING_TOOL_DIR / "training_data" / GAME_ID

    # Derived paths
    IMAGES_DIR = TRAINING_DATA_DIR / "images"
    LABELS_DIR = TRAINING_DATA_DIR / "labels"
    CLASSES_FILE = LABELS_DIR / "classes.txt"  # mlabelImg saves classes.txt in labels folder
    DATASET_YAML = TRAINING_DATA_DIR / "dataset.yaml"

    # Split dataset paths
    TRAIN_IMAGES_DIR = TRAINING_DATA_DIR / "train" / "images"
    TRAIN_LABELS_DIR = TRAINING_DATA_DIR / "train" / "labels"
    VAL_IMAGES_DIR = TRAINING_DATA_DIR / "val" / "images"
    VAL_LABELS_DIR = TRAINING_DATA_DIR / "val" / "labels"

    # Output paths
    if cfg.game_models_path:
        GAME_MODELS_DIR = Path(cfg.game_models_path)
    else:
        GAME_MODELS_DIR = PROJECT_ROOT / "GameModels" / GAME_ID


# Initialize with defaults
GAME_ID = _DEFAULT_GAME_ID
MODEL_NAME = _DEFAULT_MODEL_NAME
BASE_MODEL = _DEFAULT_BASE_MODEL

# Training data paths (will be recalculated if overrides applied)
TRAINING_DATA_DIR = TRAINING_TOOL_DIR / "training_data" / GAME_ID
IMAGES_DIR = TRAINING_DATA_DIR / "images"
LABELS_DIR = TRAINING_DATA_DIR / "labels"
CLASSES_FILE = LABELS_DIR / "classes.txt"  # mlabelImg saves classes.txt in labels folder
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
    # - 100-150: Quick experiments, small datasets, fine-tuning
    # - 200-300: Standard training, good results
    # - 300-600: Maximum accuracy, large datasets
    # Watch validation metrics - stop early if val_loss stops improving
    "epochs": 150,

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
