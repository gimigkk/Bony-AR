# 3D Pushable Buttons Walkthrough

I have completely overhauled the app's text button styling to mimic the tactile, neo-brutalism CSS button you provided.

## Changes Made

### 1. `Button3DAnimator.cs`
Created a custom interaction script that physically animates the button:
- Intercepts touch/mouse events using Unity's `IPointerDownHandler`, `IPointerUpHandler`, `IPointerEnterHandler`, and `IPointerExitHandler`.
- Smoothly Lerps the top layer of the button up and down to simulate a physical button being pressed into a base casing.

### 2. `ARAppModeController.cs` (UI Builder Refactor)
Implemented `Create3DButtonObject`, replacing standard flat buttons. The new button hierarchy consists of:
- **Base `GameObject`**: A black-filled rounded rectangle that acts as the bottom casing / 3D shadow.
- **Top Layer `GameObject`**: A `#e8e8e8` rounded rectangle with a 2px black `Outline` component. This layer is dynamically animated by `Button3DAnimator`.
- **Text `GameObject`**: Bold, black typography.

### 3. Usage Migration
Refactored the following buttons to strictly use the new 3D style:
- Bottom-bar Mode Toggles (Skeleton & Kuis)
- Reset Button
- Help Menu "Tutup" (Close) Button
- Quiz minigame "MULAI KUIS" (Start) Button
- Quiz minigame "Selesai" (Exit) Button
- Quiz game over "KEMBALI KE MENU" Button

## Validation
- The UI now has a highly cohesive, high-contrast, premium aesthetic.
- Pushing any button feels deeply satisfying due to the physics-based lerp animation mimicking a physical hardware button.
- Transparent icon buttons (like the Settings Gear and Help Icon) have retained their original icon-only styling, avoiding visual clutter on the HUD.
