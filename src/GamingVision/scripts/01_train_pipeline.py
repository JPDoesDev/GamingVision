"""
GamingVision Training Pipeline

Unified script that handles the complete training workflow:
1. Clean previous train/val splits
2. Split dataset into train/val
3. Train YOLO model
4. Export to ONNX and deploy

Usage:
    py -3.10 01_train_pipeline.py              # Run full pipeline
    py -3.10 01_train_pipeline.py --skip-to 2  # Skip to step 2 (split)
    py -3.10 01_train_pipeline.py --skip-to 3  # Skip to step 3 (train)
    py -3.10 01_train_pipeline.py --skip-to 4  # Skip to step 4 (export)

    # Automation mode (no prompts):
    py -3.10 01_train_pipeline.py --auto-yes

    # Fine-tune mode (use existing model):
    py -3.10 01_train_pipeline.py --auto-yes --fine-tune-model-path "path/to/best.pt"

    # Override paths:
    py -3.10 01_train_pipeline.py --auto-yes --training-data-path "C:\\data" --game-models-path "C:\\models"
"""

import argparse
import shutil
import sys
from datetime import datetime
from pathlib import Path

# Add scripts directory to path for imports
sys.path.insert(0, str(Path(__file__).parent))

import config
from config import (
    TRAINING_CONFIG,
    EXPORT_CONFIG,
    get_class_names,
    print_config,
    add_config_arguments,
    apply_args_to_config,
    get_runtime_config,
)


# =============================================================================
# GLOBAL STATE
# =============================================================================

# Set to True to skip all prompts (for automation)
AUTO_YES = False


# =============================================================================
# UTILITY FUNCTIONS
# =============================================================================

def prompt_yes_no(message: str, default: bool = True) -> bool:
    """Prompt user for yes/no confirmation.

    If AUTO_YES is True, always returns default without prompting.
    """
    if AUTO_YES:
        print(f"{message} [auto-yes: {'Y' if default else 'N'}]")
        return default

    suffix = " (Y/n): " if default else " (y/N): "
    while True:
        response = input(message + suffix).strip().lower()
        if response == "":
            return default
        if response in ("y", "yes"):
            return True
        if response in ("n", "no"):
            return False
        print("Please enter 'y' or 'n'")


def print_step(step_num: int, title: str):
    """Print a step header."""
    print()
    print("=" * 60)
    print(f"STEP {step_num}: {title}")
    print("=" * 60)


def print_warning(message: str):
    """Print a warning message."""
    print(f"\n[!] WARNING: {message}")


def print_success(message: str):
    """Print a success message."""
    print(f"\n[OK] {message}")


def print_error(message: str):
    """Print an error message."""
    print(f"\n[X] ERROR: {message}")


def count_files(directory: Path, pattern: str = "*") -> int:
    """Count files matching pattern in directory."""
    if not directory.exists():
        return 0
    return len(list(directory.glob(pattern)))


# =============================================================================
# STEP 1: CLEAN TRAIN/VAL FOLDERS
# =============================================================================

def step_clean() -> bool:
    """Clean train/val folders, preserving original images/labels."""
    print_step(1, "CLEAN TRAIN/VAL FOLDERS")

    # Show what will be cleaned
    train_dir = config.TRAINING_DATA_DIR / "train"
    val_dir = config.TRAINING_DATA_DIR / "val"

    train_images = count_files(config.TRAIN_IMAGES_DIR, "*.jpg") + count_files(config.TRAIN_IMAGES_DIR, "*.png")
    train_labels = count_files(config.TRAIN_LABELS_DIR, "*.txt")
    val_images = count_files(config.VAL_IMAGES_DIR, "*.jpg") + count_files(config.VAL_IMAGES_DIR, "*.png")
    val_labels = count_files(config.VAL_LABELS_DIR, "*.txt")

    print(f"\nFolders to clean:")
    print(f"  {train_dir}")
    print(f"    - images: {train_images} files")
    print(f"    - labels: {train_labels} files")
    print(f"  {val_dir}")
    print(f"    - images: {val_images} files")
    print(f"    - labels: {val_labels} files")

    total = train_images + train_labels + val_images + val_labels

    if total == 0:
        print("\nNo files to clean.")
        return True

    print(f"\nTotal: {total} files will be deleted")
    print("\n[!] Original images/labels folders will NOT be modified:")
    print(f"  {config.IMAGES_DIR}")
    print(f"  {config.LABELS_DIR}")

    if not prompt_yes_no("\nClean train/val folders?"):
        print("Skipping clean step.")
        return True

    # Perform cleaning
    try:
        if train_dir.exists():
            shutil.rmtree(train_dir)
            print(f"  Removed: {train_dir}")
        if val_dir.exists():
            shutil.rmtree(val_dir)
            print(f"  Removed: {val_dir}")

        print_success("Train/val folders cleaned successfully")
        return True
    except Exception as e:
        print_error(f"Failed to clean folders: {e}")
        return False


# =============================================================================
# STEP 2: SPLIT DATASET
# =============================================================================

def step_split() -> bool:
    """Split dataset into train/val sets."""
    print_step(2, "SPLIT DATASET")

    # Check source data
    image_count = count_files(config.IMAGES_DIR, "*.jpg") + count_files(config.IMAGES_DIR, "*.png")
    label_count = count_files(config.LABELS_DIR, "*.txt")

    print(f"\nSource data:")
    print(f"  Images: {image_count} files in {config.IMAGES_DIR}")
    print(f"  Labels: {label_count} files in {config.LABELS_DIR}")

    if image_count == 0:
        print_error("No images found! Capture and label images first.")
        return False

    if label_count == 0:
        print_error("No labels found! Label your images with LabelImg first.")
        return False

    # Check classes.txt
    if not config.CLASSES_FILE.exists():
        print_error(f"classes.txt not found at {config.CLASSES_FILE}")
        return False

    class_names = get_class_names()
    print(f"  Classes: {len(class_names)} ({', '.join(class_names)})")

    if not prompt_yes_no("\nProceed with split?"):
        print("Skipping split step.")
        return True

    # Import and run split
    try:
        from split_dataset import (
            get_labeled_images,
            clear_split_folders,
            copy_files,
            create_dataset_yaml,
            TRAIN_RATIO,
            RANDOM_SEED,
        )
        import random

        print("\nFinding labeled images...")
        labeled_images = get_labeled_images()

        if len(labeled_images) == 0:
            print_error("No labeled images found!")
            return False

        # Shuffle and split
        if RANDOM_SEED is not None:
            random.seed(RANDOM_SEED)
        random.shuffle(labeled_images)

        split_idx = int(len(labeled_images) * TRAIN_RATIO)
        train_images = labeled_images[:split_idx]
        val_images = labeled_images[split_idx:]

        print(f"\nSplitting {len(labeled_images)} images:")
        print(f"  Training:   {len(train_images)} images ({TRAIN_RATIO*100:.0f}%)")
        print(f"  Validation: {len(val_images)} images ({(1-TRAIN_RATIO)*100:.0f}%)")

        # Clear and create folders
        print("\nCreating train/val folders...")
        clear_split_folders()

        # Copy files
        print("Copying training files...")
        copy_files(train_images, config.TRAIN_IMAGES_DIR, config.TRAIN_LABELS_DIR)

        print("Copying validation files...")
        copy_files(val_images, config.VAL_IMAGES_DIR, config.VAL_LABELS_DIR)

        # Create dataset.yaml
        print("\nCreating dataset.yaml...")
        create_dataset_yaml()
        print(f"  Created: {config.DATASET_YAML}")

        print_success("Dataset split successfully")
        return True
    except Exception as e:
        print_error(f"Failed to split dataset: {e}")
        import traceback
        traceback.print_exc()
        return False


# =============================================================================
# STEP 3: TRAIN MODEL
# =============================================================================

def step_train() -> bool:
    """Train YOLO model."""
    print_step(3, "TRAIN MODEL")

    # Check prerequisites
    if not config.DATASET_YAML.exists():
        print_error(f"dataset.yaml not found at {config.DATASET_YAML}")
        print("Run the split step first.")
        return False

    train_images = count_files(config.TRAIN_IMAGES_DIR, "*.jpg") + count_files(config.TRAIN_IMAGES_DIR, "*.png")
    val_images = count_files(config.VAL_IMAGES_DIR, "*.jpg") + count_files(config.VAL_IMAGES_DIR, "*.png")

    if train_images == 0:
        print_error("No training images found! Run the split step first.")
        return False

    # Determine base model (support fine-tuning)
    runtime_cfg = get_runtime_config()
    base_model_to_use = runtime_cfg.fine_tune_model_path or config.BASE_MODEL
    is_fine_tuning = base_model_to_use != config.BASE_MODEL

    if is_fine_tuning:
        base_model_path = Path(base_model_to_use)
        if not base_model_path.exists():
            print_error(f"Fine-tune model not found: {base_model_to_use}")
            return False

    print(f"\nTraining configuration:")
    print(f"  Dataset:    {config.DATASET_YAML}")
    print(f"  Train:      {train_images} images")
    print(f"  Validation: {val_images} images")
    if is_fine_tuning:
        print(f"  Mode:       FINE-TUNING from existing model")
        print(f"  Base model: {base_model_to_use}")
    else:
        print(f"  Mode:       FULL TRAINING from scratch")
        print(f"  Base model: {config.BASE_MODEL}")
    print(f"  Epochs:     {TRAINING_CONFIG['epochs']}")
    print(f"  Image size: {TRAINING_CONFIG['imgsz']}")
    print(f"  Device:     {TRAINING_CONFIG['device']}")
    print(f"  Output:     {config.RUNS_DIR / f'{config.GAME_ID}_model'}")

    print_warning("Training may take a long time depending on dataset size and hardware.")

    if not prompt_yes_no("\nStart training?"):
        print("Skipping training step.")
        return True

    # Import and run training
    try:
        from ultralytics import YOLO

        if is_fine_tuning:
            print(f"\nFine-tuning from: {base_model_to_use}")
        else:
            print(f"\nLoading base model: {config.BASE_MODEL}")
        model = YOLO(str(base_model_to_use))

        print("Starting training...\n")
        print("-" * 60)

        # Build training arguments (matching train_model.py)
        train_args = {
            "data": str(config.DATASET_YAML),
            "device": TRAINING_CONFIG["device"],
            "epochs": TRAINING_CONFIG["epochs"],
            "imgsz": TRAINING_CONFIG["imgsz"],
            "batch": TRAINING_CONFIG["batch"],
            "patience": TRAINING_CONFIG["patience"],
            "project": str(config.RUNS_DIR),
            "name": f"{config.GAME_ID}_model",
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

        print("-" * 60)
        print_success("Training completed successfully")

        # Show results location
        results_dir = config.RUNS_DIR / f"{config.GAME_ID}_model"
        print(f"\nResults saved to: {results_dir}")
        print(f"  Best model: {results_dir / 'weights' / 'best.pt'}")
        print(f"  Last model: {results_dir / 'weights' / 'last.pt'}")

        return True
    except Exception as e:
        print_error(f"Training failed: {e}")
        import traceback
        traceback.print_exc()
        return False


# =============================================================================
# STEP 4: EXPORT MODEL
# =============================================================================

def step_export() -> bool:
    """Export model to ONNX and deploy (both 640 and 1440 sizes), plus .pt for fine-tuning."""
    print_step(4, "EXPORT MODEL")

    # Find trained model
    weights_dir = config.RUNS_DIR / f"{config.GAME_ID}_model" / "weights"
    best_pt = weights_dir / "best.pt"
    last_pt = weights_dir / "last.pt"

    if not best_pt.exists() and not last_pt.exists():
        print_error(f"No trained model found in {weights_dir}")
        print("Run the training step first.")
        return False

    weights_path = best_pt if best_pt.exists() else last_pt

    # Export both sizes
    export_sizes = [640, 1440]

    print(f"\nModel to export:")
    print(f"  Weights: {weights_path}")
    print(f"  Format:  ONNX (opset {EXPORT_CONFIG['opset']})")
    print(f"  Sizes:   {', '.join(f'{s}x{s}' for s in export_sizes)}")

    # Generate version stamp
    version = datetime.now().strftime("%Y%m%d_%H%M%S")

    versioned_base = f"{config.MODEL_NAME}_v{version}"

    print(f"\nOutput files:")
    print(f"  {config.GAME_MODELS_DIR / f'{versioned_base}.pt'} (for fine-tuning)")
    for size in export_sizes:
        versioned_name = f"{versioned_base}_{size}"
        print(f"  {config.GAME_MODELS_DIR / f'{versioned_name}.onnx'}")
        print(f"  {config.GAME_MODELS_DIR / f'{versioned_name}.txt'}")

    if not prompt_yes_no("\nExport model?"):
        print("Skipping export step.")
        return True

    try:
        from ultralytics import YOLO

        # Ensure output directory exists
        config.GAME_MODELS_DIR.mkdir(parents=True, exist_ok=True)

        # Copy .pt file for fine-tuning (versioned name)
        target_pt = config.GAME_MODELS_DIR / f"{versioned_base}.pt"
        shutil.copy2(weights_path, target_pt)
        pt_size = target_pt.stat().st_size / (1024 * 1024)
        print(f"\nCopied PyTorch model for fine-tuning:")
        print(f"  {target_pt} ({pt_size:.1f} MB)")

        deployed_models = []
        deployed_pt_name = f"{versioned_base}.pt"

        for imgsz in export_sizes:
            print()
            print("-" * 40)
            print(f"Exporting {imgsz}x{imgsz} model...")
            print("-" * 40)

            # Load model fresh for each export
            print(f"Loading model: {weights_path}")
            model = YOLO(str(weights_path))

            print(f"Exporting to ONNX...")
            export_path = model.export(
                format=EXPORT_CONFIG["format"],
                opset=EXPORT_CONFIG["opset"],
                simplify=EXPORT_CONFIG["simplify"],
                dynamic=EXPORT_CONFIG["dynamic"],
                imgsz=imgsz,
            )

            export_path = Path(export_path)
            print(f"  Exported: {export_path}")

            versioned_name = f"{versioned_base}_{imgsz}"
            target_onnx = config.GAME_MODELS_DIR / f"{versioned_name}.onnx"
            target_labels = config.GAME_MODELS_DIR / f"{versioned_name}.txt"

            shutil.copy2(export_path, target_onnx)
            print(f"  Copied: {target_onnx}")

            # Create labels file
            class_names = get_class_names()
            with open(target_labels, 'w') as f:
                f.write('\n'.join(class_names))
            print(f"  Created: {target_labels}")

            # Clean up temp export in weights folder
            if export_path.exists() and export_path != target_onnx:
                export_path.unlink()

            onnx_size = target_onnx.stat().st_size / (1024 * 1024)
            print(f"  Size: {onnx_size:.1f} MB")

            deployed_models.append((versioned_name, imgsz))

        # Update game_config.json with model and classes
        config_path = config.GAME_MODELS_DIR / "game_config.json"
        if config_path.exists():
            import json
            with open(config_path, 'r') as f:
                game_cfg = json.load(f)

            # Use 640 model as default
            default_model = next(
                (name for name, size in deployed_models if size == 640),
                deployed_models[0][0]
            )

            old_model = game_cfg.get("modelFile", "")
            game_cfg["modelFile"] = f"{default_model}.onnx"

            # Add classes from training to game_config
            class_names = get_class_names()
            existing_labels = {label.get("name") for label in game_cfg.get("labels", [])}
            existing_primary = set(game_cfg.get("primaryLabels", []))
            existing_secondary = set(game_cfg.get("secondaryLabels", []))
            existing_tertiary = set(game_cfg.get("tertiaryLabels", []))
            all_existing_tiers = existing_primary | existing_secondary | existing_tertiary

            # Initialize arrays if they don't exist
            if "labels" not in game_cfg:
                game_cfg["labels"] = []
            if "primaryLabels" not in game_cfg:
                game_cfg["primaryLabels"] = []

            # Add new classes
            new_classes_added = []
            for class_name in class_names:
                # Add to labels array if not already there
                if class_name not in existing_labels:
                    game_cfg["labels"].append({
                        "name": class_name,
                        "description": ""
                    })

                # Add to primaryLabels if not in any tier
                if class_name not in all_existing_tiers:
                    game_cfg["primaryLabels"].append(class_name)
                    new_classes_added.append(class_name)

            with open(config_path, 'w') as f:
                json.dump(game_cfg, f, indent=2)

            print(f"\nUpdated game_config.json:")
            print(f"  modelFile: {old_model} -> {default_model}.onnx")
            if new_classes_added:
                print(f"  Added {len(new_classes_added)} new classes to primaryLabels:")
                for cls in new_classes_added:
                    print(f"    - {cls}")
            else:
                print(f"  Classes: All {len(class_names)} classes already configured")
        else:
            print_warning(f"game_config.json not found at {config_path}")
            print("You'll need to create this file manually.")

        # Final summary
        print()
        print_success("Models exported successfully!")
        print("\nExported files:")
        print(f"  {deployed_pt_name} (PyTorch - for fine-tuning)")
        for name, size in deployed_models:
            mode = "(Speed - 30 FPS)" if size == 640 else "(Accuracy - higher detail)"
            print(f"  {name}.onnx {mode}")

        return True
    except Exception as e:
        print_error(f"Export failed: {e}")
        import traceback
        traceback.print_exc()
        return False


# =============================================================================
# MAIN PIPELINE
# =============================================================================

def main():
    global AUTO_YES

    parser = argparse.ArgumentParser(
        description="GamingVision Training Pipeline",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Steps:
  1. Clean    - Remove previous train/val splits
  2. Split    - Split dataset into train/val
  3. Train    - Train YOLO model
  4. Export   - Export to ONNX and deploy

Examples:
  py -3.10 01_train_pipeline.py              # Run full pipeline
  py -3.10 01_train_pipeline.py --skip-to 3  # Skip to training
  py -3.10 01_train_pipeline.py --skip-to 4  # Skip to export only
  py -3.10 01_train_pipeline.py --auto-yes   # No prompts (automation)
        """
    )
    parser.add_argument(
        "--skip-to",
        type=int,
        choices=[1, 2, 3, 4],
        default=1,
        help="Skip to step number (1=clean, 2=split, 3=train, 4=export)"
    )
    parser.add_argument(
        "--auto-yes",
        action="store_true",
        help="Skip all confirmation prompts (for automation)"
    )

    # Add config override arguments
    add_config_arguments(parser)

    args = parser.parse_args()

    # Set global auto-yes mode
    AUTO_YES = args.auto_yes

    # Apply config overrides from CLI arguments
    apply_args_to_config(args)

    # Print header
    print()
    print("=" * 60)
    print("GamingVision Training Pipeline")
    print("=" * 60)

    if AUTO_YES:
        print("\n[AUTO-YES MODE: All prompts will be auto-confirmed]")

    # Print configuration
    print_config()

    # Show pipeline overview
    print("\nPipeline steps:")
    steps = [
        (1, "Clean", "Remove previous train/val splits"),
        (2, "Split", "Split dataset into train/val"),
        (3, "Train", "Train YOLO model"),
        (4, "Export", "Export to ONNX and deploy"),
    ]

    for num, name, desc in steps:
        marker = "->" if num >= args.skip_to else "o"
        print(f"  {marker} Step {num}: {name} - {desc}")

    if args.skip_to > 1:
        print(f"\n[!] Starting from step {args.skip_to}")

    if not prompt_yes_no("\nBegin pipeline?"):
        print("\nPipeline cancelled.")
        return

    # Run pipeline steps
    success = True

    if args.skip_to <= 1:
        success = step_clean()
        if not success:
            print_error("Pipeline failed at step 1 (Clean)")
            sys.exit(1)

    if args.skip_to <= 2 and success:
        success = step_split()
        if not success:
            print_error("Pipeline failed at step 2 (Split)")
            sys.exit(1)

    if args.skip_to <= 3 and success:
        success = step_train()
        if not success:
            print_error("Pipeline failed at step 3 (Train)")
            sys.exit(1)

    if args.skip_to <= 4 and success:
        success = step_export()
        if not success:
            print_error("Pipeline failed at step 4 (Export)")
            sys.exit(1)

    # Final summary
    print()
    print("=" * 60)
    print("PIPELINE COMPLETE")
    print("=" * 60)
    print()
    print("Your model is ready to use in GamingVision!")
    print()
    print("Next steps:")
    print(f"  1. Review {config.GAME_MODELS_DIR / 'game_config.json'}")
    print("  2. Configure label tiers (primaryLabels, secondaryLabels, etc.)")
    print("  3. Run GamingVision:")
    print("     dotnet run -c Release --project src\\GamingVision")
    print()


if __name__ == "__main__":
    main()
