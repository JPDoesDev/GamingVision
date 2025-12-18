"""
Split dataset into training and validation sets.

This script:
1. Reads images from the images/ folder
2. Randomly splits them into train (80%) and val (20%) sets
3. Copies images and labels to train/ and val/ subfolders
4. Creates dataset.yaml for YOLO training

Run this AFTER labeling images with LabelImg.

Usage:
    py -3.9 split_dataset.py
"""

import os
import shutil
import random
from pathlib import Path
import yaml

from config import (
    GAME_ID,
    TRAINING_DATA_DIR,
    IMAGES_DIR,
    LABELS_DIR,
    CLASSES_FILE,
    DATASET_YAML,
    TRAIN_IMAGES_DIR,
    TRAIN_LABELS_DIR,
    VAL_IMAGES_DIR,
    VAL_LABELS_DIR,
    get_class_names,
    validate_paths,
)

# Train/validation split ratio
TRAIN_RATIO = 0.8

# Random seed for reproducibility (set to None for random each time)
RANDOM_SEED = 42


def get_labeled_images() -> list[str]:
    """Get list of images that have corresponding label files."""
    labeled = []

    for img_path in IMAGES_DIR.iterdir():
        if img_path.suffix.lower() not in ['.jpg', '.jpeg', '.png']:
            continue

        # Check if label file exists
        label_path = LABELS_DIR / (img_path.stem + '.txt')
        if label_path.exists():
            # Check if label file has content (not empty)
            content = label_path.read_text().strip()
            if content:
                labeled.append(img_path.name)
            else:
                print(f"  Skipping {img_path.name} (empty label file)")
        else:
            print(f"  Skipping {img_path.name} (no label file)")

    return labeled


def clear_split_folders():
    """Remove existing train/val folders."""
    for folder in [TRAIN_IMAGES_DIR, TRAIN_LABELS_DIR, VAL_IMAGES_DIR, VAL_LABELS_DIR]:
        if folder.exists():
            shutil.rmtree(folder)
        folder.mkdir(parents=True, exist_ok=True)


def copy_files(image_names: list[str], dest_images: Path, dest_labels: Path):
    """Copy images and their labels to destination folders."""
    for img_name in image_names:
        # Copy image
        src_img = IMAGES_DIR / img_name
        dst_img = dest_images / img_name
        shutil.copy2(src_img, dst_img)

        # Copy label
        label_name = Path(img_name).stem + '.txt'
        src_label = LABELS_DIR / label_name
        dst_label = dest_labels / label_name
        if src_label.exists():
            shutil.copy2(src_label, dst_label)


def create_dataset_yaml():
    """Create dataset.yaml configuration file for YOLO training."""
    class_names = get_class_names()

    # Build names dict
    names = {i: name for i, name in enumerate(class_names)}

    dataset_config = {
        'path': str(TRAINING_DATA_DIR.as_posix()),
        'train': 'train/images',
        'val': 'val/images',
        'names': names,
    }

    with open(DATASET_YAML, 'w') as f:
        yaml.dump(dataset_config, f, default_flow_style=False, sort_keys=False)

    print(f"Created {DATASET_YAML}")


def main():
    print("=" * 60)
    print(f"Dataset Splitter for: {GAME_ID}")
    print("=" * 60)
    print()

    # Validate paths
    if not validate_paths():
        print("\nPlease fix the errors above before continuing.")
        return

    # Get labeled images
    print("\nFinding labeled images...")
    labeled_images = get_labeled_images()

    if len(labeled_images) == 0:
        print("\nERROR: No labeled images found!")
        print("Please label your images with LabelImg first.")
        return

    print(f"\nFound {len(labeled_images)} labeled images")

    # Shuffle and split
    if RANDOM_SEED is not None:
        random.seed(RANDOM_SEED)
    random.shuffle(labeled_images)

    split_idx = int(len(labeled_images) * TRAIN_RATIO)
    train_images = labeled_images[:split_idx]
    val_images = labeled_images[split_idx:]

    print(f"  Training set:   {len(train_images)} images ({TRAIN_RATIO*100:.0f}%)")
    print(f"  Validation set: {len(val_images)} images ({(1-TRAIN_RATIO)*100:.0f}%)")

    if len(val_images) < 5:
        print("\nWARNING: Very small validation set!")
        print("Consider collecting more training data for better results.")

    # Clear and create folders
    print("\nCreating train/val folders...")
    clear_split_folders()

    # Copy files
    print("Copying training files...")
    copy_files(train_images, TRAIN_IMAGES_DIR, TRAIN_LABELS_DIR)

    print("Copying validation files...")
    copy_files(val_images, VAL_IMAGES_DIR, VAL_LABELS_DIR)

    # Create dataset.yaml
    print("\nCreating dataset.yaml...")
    create_dataset_yaml()

    # Summary
    print()
    print("=" * 60)
    print("Dataset split complete!")
    print("=" * 60)
    print(f"  Train images: {TRAIN_IMAGES_DIR}")
    print(f"  Train labels: {TRAIN_LABELS_DIR}")
    print(f"  Val images:   {VAL_IMAGES_DIR}")
    print(f"  Val labels:   {VAL_LABELS_DIR}")
    print(f"  Config:       {DATASET_YAML}")
    print()
    print("Next step: Run train_model.py to start training")


if __name__ == "__main__":
    main()
