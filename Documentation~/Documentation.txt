# 3D to 2D Converter

Professional Unity Editor Extension to convert 3D FBX models and animations into high-quality 2D Sprite Sheets.

## Features

- **Auto Material Mapping**: Automatically detects and remapps existing materials in your Assets folder.
- **Heuristic Texture Matching**: Intelligent texture search for models with missing material links.
- **Optimized Rendering**: Uses `PreviewRenderUtility` for background capture without affecting the scene.
- **Consistent FPS**: Frame-by-frame sampling at custom FPS (default 30) for smooth 2D animations.
- **Auto Frame**: Automatically calculates camera distance and zoom to fit the entire animation without clipping.
- **FullRect Mesh**: Ensures sprites are exported with consistent bounding boxes to prevent jittering.

## How to Use

1. Open the tool via `Tools > 3D to 2D Converter`.
2. Click **Browse FBX** to select your model.
3. Select the animation clip you want to convert.
4. Adjust camera settings or use **Auto Frame**.
5. Click **CAPTURE ANIMATION**.
6. The results will be saved in `Assets/3dto2d/Exports/`.

## License

© 2026 Shapemaster. All rights reserved.
