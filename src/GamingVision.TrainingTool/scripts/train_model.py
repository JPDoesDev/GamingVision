"""
Train YOLO model for GamingVision.

This script:
1. Loads a pretrained YOLOv11 model (or fine-tunes from existing best.pt)
2. Trains it on your labeled dataset
3. Saves the best model to runs/detect/{game_id}_model/

Prerequisites:
    1. Label images with LabelImg
    2. Run split_dataset.py to create train/val split

Usage:
    py -3.10 train_model.py

    # Or with custom epochs:
    py -3.10 train_model.py --epochs 200

    # Resume interrupted training:
    py -3.10 train_model.py --resume

    # Skip confirmation prompts (for automation):
    py -3.10 train_model.py --auto-yes

    # Fine-tune from existing model:
    py -3.10 train_model.py --fine-tune-model-path "path/to/best.pt"

    # Override paths via CLI:
    py -3.10 train_model.py --game-id arc_raiders --training-data-path "C:\\data"
"""

import argparse
import sys
from pathlib import Path

# Add scripts directory to path for imports
sys.path.insert(0, str(Path(__file__).parent))

from config import (
    GAME_ID,
    MODEL_NAME,
    BASE_MODEL,
    DATASET_YAML,
    RUNS_DIR,
    TRAINING_CONFIG,
    print_config,
    validate_paths,
    get_class_names,
    add_config_arguments,
    apply_args_to_config,
    get_runtime_config,
)


def check_prerequisites():
    """Check that dataset is ready for training."""
    errors = []

    if not DATASET_YAML.exists():
        errors.append(f"dataset.yaml not found: {DATASET_YAML}")
        errors.append("Run split_dataset.py first to prepare the dataset.")

    if errors:
        print("ERROR: Prerequisites not met:")
        for error in errors:
            print(f"  - {error}")
        return False

    return True


def train_model(epochs: int = None, resume: bool = False, fine_tune_model_path: str = None):
    """Train the YOLO model.

    Args:
        epochs: Number of training epochs (None = use config default)
        resume: Resume from last checkpoint
        fine_tune_model_path: Path to existing best.pt for fine-tuning
    """
    try:
        from ultralytics import YOLO
    except ImportError:
        print("ERROR: ultralytics not installed!")
        print("Run: py -3.10 -m pip install ultralytics")
        return None

    # Re-import to get potentially updated values after apply_args_to_config
    from config import GAME_ID, BASE_MODEL, RUNS_DIR

    # Use custom epochs if provided
    training_epochs = epochs if epochs else TRAINING_CONFIG["epochs"]

    # Project name for this training run
    project_name = f"{GAME_ID}_model"
    output_dir = RUNS_DIR / project_name

    if resume:
        # Resume from last checkpoint
        last_weights = output_dir / "weights" / "last.pt"
        if not last_weights.exists():
            print(f"ERROR: Cannot resume - no checkpoint found at {last_weights}")
            return None

        print(f"\nResuming training from {last_weights}")
        model = YOLO(str(last_weights))
        results = model.train(resume=True)
    else:
        # Determine base model: fine-tune path > runtime config > default
        runtime_cfg = get_runtime_config()
        base_model_to_use = fine_tune_model_path or runtime_cfg.fine_tune_model_path or BASE_MODEL

        # Check if fine-tuning from existing model
        if base_model_to_use != BASE_MODEL:
            base_path = Path(base_model_to_use)
            if not base_path.exists():
                print(f"ERROR: Fine-tune model not found: {base_model_to_use}")
                return None
            print(f"\nFine-tuning from existing model: {base_model_to_use}")
        else:
            print(f"\nLoading base model: {base_model_to_use}")

        model = YOLO(str(base_model_to_use))

        print(f"Starting training for {training_epochs} epochs...")
        print(f"Output directory: {output_dir}")
        print()

        # Build training arguments
        train_args = {
            "data": str(DATASET_YAML),
            "device": TRAINING_CONFIG["device"],
            "epochs": training_epochs,
            "imgsz": TRAINING_CONFIG["imgsz"],
            "batch": TRAINING_CONFIG["batch"],
            "patience": TRAINING_CONFIG["patience"],
            "project": str(RUNS_DIR),
            "name": project_name,
            "exist_ok": True,

            # Learning rate
            "lr0": TRAINING_CONFIG["lr0"],
            "weight_decay": TRAINING_CONFIG["weight_decay"],
            "warmup_epochs": TRAINING_CONFIG["warmup_epochs"],
            "cos_lr": TRAINING_CONFIG["cos_lr"],

            # Augmentation
            "augment": TRAINING_CONFIG["augment"],
            "hsv_h": TRAINING_CONFIG["hsv_h"],
            "hsv_s": TRAINING_CONFIG["hsv_s"],
            "hsv_v": TRAINING_CONFIG["hsv_v"],
            "degrees": TRAINING_CONFIG["degrees"],
            "translate": TRAINING_CONFIG["translate"],
            "scale": TRAINING_CONFIG["scale"],
            "fliplr": TRAINING_CONFIG["fliplr"],
            "flipud": TRAINING_CONFIG["flipud"],
            "mosaic": TRAINING_CONFIG["mosaic"],
            "mixup": TRAINING_CONFIG["mixup"],
            "close_mosaic": TRAINING_CONFIG["close_mosaic"],

            # Performance
            "workers": TRAINING_CONFIG["workers"],
            "cache": TRAINING_CONFIG["cache"],
            "amp": TRAINING_CONFIG["amp"],

            # Output
            "verbose": True,
            "plots": True,
            "save": True,
        }

        results = model.train(**train_args)

    return results


def print_results(output_dir: Path):
    """Print training results and next steps."""
    best_weights = output_dir / "weights" / "best.pt"
    last_weights = output_dir / "weights" / "last.pt"

    print()
    print("=" * 60)
    print("Training Complete!")
    print("=" * 60)
    print()

    if best_weights.exists():
        size_mb = best_weights.stat().st_size / (1024 * 1024)
        print(f"Best model:  {best_weights}")
        print(f"Model size:  {size_mb:.1f} MB")

    print()
    print("Training artifacts:")
    print(f"  Weights:     {output_dir / 'weights'}")
    print(f"  Results:     {output_dir / 'results.png'}")
    print(f"  Confusion:   {output_dir / 'confusion_matrix.png'}")

    print()
    print("Next steps:")
    print("  1. Review results.png and confusion_matrix.png")
    print("  2. Run export_model.py to export to ONNX and deploy")
    print()


def main():
    parser = argparse.ArgumentParser(description="Train YOLO model for GamingVision")
    parser.add_argument("--epochs", type=int, help="Number of training epochs")
    parser.add_argument("--resume", action="store_true", help="Resume interrupted training")
    parser.add_argument("--auto-yes", action="store_true", help="Skip confirmation prompts (for automation)")

    # Add config override arguments
    add_config_arguments(parser)

    args = parser.parse_args()

    # Apply config overrides from CLI arguments
    apply_args_to_config(args)

    # Re-import to get updated values
    from config import GAME_ID, RUNS_DIR, DATASET_YAML

    # Print configuration
    print_config()
    print()

    # Validate paths
    print("Validating paths...")
    if not validate_paths():
        print("\nPlease fix the errors above before continuing.")
        sys.exit(1)

    # Check prerequisites
    if not args.resume and not check_prerequisites():
        sys.exit(1)

    # Print class information
    print()
    print("Classes to detect:")
    for i, name in enumerate(get_class_names()):
        print(f"  {i}: {name}")
    print()

    # Confirm before training (skip if --auto-yes)
    if not args.resume and not args.auto_yes:
        response = input("Start training? [Y/n]: ").strip().lower()
        if response and response != 'y':
            print("Training cancelled.")
            sys.exit(0)

    # Train
    results = train_model(
        epochs=args.epochs,
        resume=args.resume,
        fine_tune_model_path=getattr(args, 'fine_tune_model_path', None)
    )

    if results is not None:
        output_dir = RUNS_DIR / f"{GAME_ID}_model"
        print_results(output_dir)


if __name__ == "__main__":
    main()
