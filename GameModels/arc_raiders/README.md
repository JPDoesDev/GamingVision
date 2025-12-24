# ARC Raiders - GamingVision Model

## Model Information
- **Model File:** ArcRaidersModel_v20251220_214214.onnx
- **Base Architecture:** YOLOv11n
- **Input Size:** 1440x1440
- **Training Data:** Custom labeled screenshots from ARC Raiders

## Class Labels

The model was trained to detect 5 classes (defined in `ArcRaidersModel_v20251220_214214.txt`):

| Index | Label | Description |
|-------|-------|-------------|
| 0 | `inv-title` | Inventory item title when hovering over items |
| 1 | `inv-info` | Inventory item description and details |
| 2 | `quick_menu` | Quick menu elements and options |
| 3 | `st-title` | Skill tree title elements |
| 4 | `st-info` | Skill tree item information |

## Recommended Configuration

**Primary Labels** (auto-read, quick access):
- `inv-title` - Inventory item titles
- `quick_menu` - Quick menu options
- `st-title` - Skill tree titles

**Secondary Labels** (manual read, detailed info):
- `inv-info` - Detailed inventory item descriptions
- `st-info` - Skill tree details
