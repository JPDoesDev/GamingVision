"""
Export trained YOLO model to ONNX and deploy to GamingVision.

This script:
1. Loads the best trained model weights
2. Exports to ONNX format at multiple sizes (640 for speed, 1440 for accuracy)
3. Copies ONNX models to GameModels/{game_id}/
4. Creates label file for GamingVision
5. Optionally updates game_config.json
6. Copies best.pt to GameModels for future fine-tuning

Prerequisites:
    Complete training with train_model.py

Usage:
    py -3.10 export_model.py

    # Use last.pt instead of best.pt:
    py -3.10 export_model.py --use-last

    # Skip copying to GameModels:
    py -3.10 export_model.py --no-deploy

    # Export only specific size (640 or 1440):
    py -3.10 export_model.py --size 640

    # Override paths via CLI:
    py -3.10 export_model.py --game-models-path "C:\\GameModels\\arc_raiders"

    # Specify custom weights path:
    py -3.10 export_model.py --weights-path "path/to/best.pt"
"""

import argparse
import json
import shutil
import sys
from datetime import datetime
from pathlib import Path

# Add scripts directory to path for imports
sys.path.insert(0, str(Path(__file__).parent))

from config import (
    GAME_ID,
    MODEL_NAME,
    RUNS_DIR,
    GAME_MODELS_DIR,
    CLASSES_FILE,
    EXPORT_CONFIG,
    get_class_names,
    print_config,
    add_config_arguments,
    apply_args_to_config,
    get_runtime_config,
)


def generate_version_stamp() -> str:
    """Generate a version stamp based on current datetime."""
    return datetime.now().strftime("%Y%m%d_%H%M%S")


def find_trained_model(use_last: bool = False) -> Path:
    """Find the trained model weights."""
    model_dir = RUNS_DIR / f"{GAME_ID}_model" / "weights"

    if use_last:
        weights_file = model_dir / "last.pt"
    else:
        weights_file = model_dir / "best.pt"

    if not weights_file.exists():
        raise FileNotFoundError(f"Model weights not found: {weights_file}")

    return weights_file


def export_to_onnx(weights_path: Path, imgsz: int) -> Path:
    """Export PyTorch model to ONNX format at specified size."""
    try:
        from ultralytics import YOLO
    except ImportError:
        print("ERROR: ultralytics not installed!")
        print("Run: py -3.10 -m pip install ultralytics")
        sys.exit(1)

    print(f"\nLoading model: {weights_path}")
    model = YOLO(str(weights_path))

    print(f"Exporting to ONNX at {imgsz}x{imgsz}...")
    print(f"  Format:    {EXPORT_CONFIG['format']}")
    print(f"  Opset:     {EXPORT_CONFIG['opset']}")
    print(f"  Simplify:  {EXPORT_CONFIG['simplify']}")
    print(f"  Image size: {imgsz}")

    # Export
    export_path = model.export(
        format=EXPORT_CONFIG["format"],
        opset=EXPORT_CONFIG["opset"],
        simplify=EXPORT_CONFIG["simplify"],
        dynamic=EXPORT_CONFIG["dynamic"],
        imgsz=imgsz,
    )

    return Path(export_path)


def deploy_to_game_models(onnx_path: Path, version: str, imgsz: int):
    """Copy ONNX model and labels to GameModels folder."""
    # Ensure GameModels directory exists
    GAME_MODELS_DIR.mkdir(parents=True, exist_ok=True)

    # Versioned filename with size: ModelName_v20251220_143052_640.onnx
    versioned_name = f"{MODEL_NAME}_v{version}_{imgsz}"

    # Target paths
    target_onnx = GAME_MODELS_DIR / f"{versioned_name}.onnx"
    target_labels = GAME_MODELS_DIR / f"{versioned_name}.txt"

    # Copy ONNX model
    print(f"\nCopying ONNX model to: {target_onnx}")
    shutil.copy2(onnx_path, target_onnx)

    # Copy/create labels file
    print(f"Creating labels file: {target_labels}")
    class_names = get_class_names()
    with open(target_labels, 'w') as f:
        f.write('\n'.join(class_names))

    # Get file sizes
    onnx_size = target_onnx.stat().st_size / (1024 * 1024)

    print(f"\nDeployed files:")
    print(f"  Model:  {target_onnx.name} ({onnx_size:.1f} MB)")
    print(f"  Labels: {target_labels.name} ({len(class_names)} classes)")

    return target_onnx, target_labels, versioned_name


def update_game_config(model_filename: str):
    """Update game_config.json with new model filename."""
    config_path = GAME_MODELS_DIR / "game_config.json"

    if not config_path.exists():
        print(f"\nWARNING: game_config.json not found at {config_path}")
        print("You'll need to create this file manually or via the Training Tool.")
        return

    print(f"\nUpdating game_config.json...")

    with open(config_path, 'r') as f:
        config = json.load(f)

    # Update model file reference
    old_model = config.get("modelFile", "")
    config["modelFile"] = model_filename

    with open(config_path, 'w') as f:
        json.dump(config, f, indent=2)

    if old_model != model_filename:
        print(f"  Updated modelFile: {old_model} -> {model_filename}")
    else:
        print(f"  modelFile already set to: {model_filename}")


def suggest_label_config():
    """Suggest label tier configuration."""
    class_names = get_class_names()

    print()
    print("=" * 60)
    print("Label Configuration Suggestions")
    print("=" * 60)
    print()
    print("Update game_config.json with your label tiers:")
    print()
    print('  "primaryLabels": [],     // Auto-read labels (most important)')
    print('  "secondaryLabels": [],   // On-demand labels (Alt+2)')
    print('  "tertiaryLabels": [],    // On-demand labels (Alt+3)')
    print('  "labelPriority": [],     // Reading order for auto-read')
    print()
    print("Available labels:")
    for name in class_names:
        print(f'    "{name}",')
    print()


def main():
    parser = argparse.ArgumentParser(description="Export YOLO model to ONNX")
    parser.add_argument("--use-last", action="store_true",
                        help="Use last.pt instead of best.pt")
    parser.add_argument("--no-deploy", action="store_true",
                        help="Export only, don't copy to GameModels")
    parser.add_argument("--size", type=int, choices=[640, 1440],
                        help="Export only specific size (default: both 640 and 1440)")
    parser.add_argument("--weights-path", type=str,
                        help="Path to weights file (overrides auto-detection)")
    parser.add_argument("--save-pt", action="store_true", default=True,
                        help="Save best.pt to GameModels for future fine-tuning (default: True)")
    parser.add_argument("--no-save-pt", action="store_true",
                        help="Don't save best.pt to GameModels")

    # Add config override arguments
    add_config_arguments(parser)

    args = parser.parse_args()

    # Apply config overrides from CLI arguments
    apply_args_to_config(args)

    # Re-import to get updated values
    from config import GAME_ID, MODEL_NAME, RUNS_DIR, GAME_MODELS_DIR

    # Determine which sizes to export
    if args.size:
        export_sizes = [args.size]
    else:
        export_sizes = [640, 1440]  # Export both by default

    # Print configuration
    print_config()
    print(f"\nExport sizes: {export_sizes}")

    # Find trained model
    print("\nLooking for trained model...")
    try:
        if args.weights_path:
            weights_path = Path(args.weights_path)
            if not weights_path.exists():
                raise FileNotFoundError(f"Weights file not found: {weights_path}")
        else:
            weights_path = find_trained_model(use_last=args.use_last)
        print(f"Found: {weights_path}")
    except FileNotFoundError as e:
        print(f"ERROR: {e}")
        print("\nMake sure you've trained a model first with train_model.py")
        sys.exit(1)

    # Generate single version stamp for all exports
    version = generate_version_stamp()
    deployed_models = []

    # Export at each size
    for imgsz in export_sizes:
        print()
        print("=" * 60)
        print(f"Exporting {imgsz}x{imgsz} model...")
        print("=" * 60)

        # Export to ONNX
        onnx_path = export_to_onnx(weights_path, imgsz)
        print(f"Exported: {onnx_path}")

        # Deploy to GameModels
        if not args.no_deploy:
            target_onnx, target_labels, versioned_name = deploy_to_game_models(onnx_path, version, imgsz)
            deployed_models.append((versioned_name, imgsz))

            # Clean up the temp export in weights folder
            if onnx_path.exists() and onnx_path != target_onnx:
                onnx_path.unlink()

    # Update game_config to use 640 (speed) model by default
    if not args.no_deploy and deployed_models:
        # Find the 640 model, or use the first one
        default_model = next(
            (name for name, size in deployed_models if size == 640),
            deployed_models[0][0]
        )
        update_game_config(f"{default_model}.onnx")
        suggest_label_config()

    # Save best.pt to GameModels for future fine-tuning
    if not args.no_deploy and not args.no_save_pt:
        print("\nSaving best.pt for future fine-tuning...")
        target_pt = GAME_MODELS_DIR / "best.pt"
        shutil.copy2(weights_path, target_pt)
        print(f"  Saved: {target_pt}")
        print("  (Use this with --fine-tune-model-path for incremental training)")

    # Final summary
    print()
    print("=" * 60)
    print("Export Complete!")
    print("=" * 60)
    print()

    if not args.no_deploy and deployed_models:
        print("Exported models:")
        for name, size in deployed_models:
            mode = "(Speed - 30 FPS)" if size == 640 else "(Accuracy - higher detail)"
            print(f"  {name}.onnx {mode}")
        print()
        print("The 640 (speed) model is set as default in game_config.json.")
        print()
        print("To switch models, edit game_config.json and change 'modelFile' to:")
        for name, size in deployed_models:
            print(f'  "{name}.onnx"  # {size}x{size}')
        print()
        print("Your model is now ready to use in GamingVision!")
        print()
        print("Next steps:")
        print(f"  1. Edit {GAME_MODELS_DIR / 'game_config.json'}")
        print("  2. Configure primaryLabels, secondaryLabels, tertiaryLabels")
        print("  3. Run GamingVision and select your game")
        print()
        print("Commands:")
        print("  # Run GamingVision")
        print("  dotnet run -c Release --project src\\GamingVision")
    else:
        print("ONNX models exported (not deployed)")
        print()
        print("To deploy manually, copy .onnx files to GameModels folder")


if __name__ == "__main__":
    main()
