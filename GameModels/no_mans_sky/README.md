# No Man's Sky - GamingVision Model

## Model Information
- **Model File:** NoMansModel.onnx
- **Base Architecture:** YOLOv8n
- **Input Size:** 640x640
- **Training Data:** Custom labeled screenshots from No Man's Sky

## Class Labels

The model was trained to detect 8 classes (defined in `NoMansModel.txt`):

| Index | Label | Description |
|-------|-------|-------------|
| 0 | `title` | Title block of menu items when cursor hovers over an item |
| 1 | `info` | Main text block in menu items, dialogue blocks, or new quest blocks |
| 2 | `other` | Miscellaneous detected elements |
| 3 | `controls` | Control prompts and button indicators |
| 4 | `item` | Popup in center of screen when hovering over in-world minerals or items |
| 5 | `quest` | Quest block in the bottom right of the screen |
| 6 | `junk` | Low-priority or irrelevant UI elements |
| 7 | `menu_labeld` | Labeled menu elements |

## Recommended Configuration

**Primary Labels** (auto-read, quick access):
- `title` - Menu item titles
- `item` - In-world item popups

**Secondary Labels** (manual read, detailed info):
- `info` - Detailed text content
- `quest` - Quest log information
- `controls` - Control prompts
- `menu_labeld` - Menu labels

## Extracting Labels from ONNX Models

If you need to verify or extract class labels from an ONNX model, use this Python script:

```python
import onnx

model = onnx.load('NoMansModel.onnx')
for prop in model.metadata_props:
    if prop.key == 'names':
        print(f"Classes: {prop.value}")
```

## Original Project

This model was originally developed for the [NoMansAccess](https://github.com/JPDoesDev/NoMansAccess) Python project.
