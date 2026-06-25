# Restyle all Buttons to 3D Pushable CSS Design

The goal is to recreate the specific 3D pushable button CSS style using Unity's uGUI system and apply it to all text buttons across the app.

## Proposed Changes

### 1. New Custom Animator Script
#### [NEW] `Assets/Button3DAnimator.cs`
A new script that implements `IPointerDownHandler`, `IPointerUpHandler`, `IPointerEnterHandler`, and `IPointerExitHandler`. 
It will smoothly lerp the `anchoredPosition.y` of the button's top layer to create the physical 3D push effect:
- **Idle**: `+6` pixels up
- **Hover**: `+10` pixels up (pops up towards the user)
- **Active (Pressed)**: `0` pixels (pushes down flush into the base)

### 2. Refactor Button Creation Logic
#### [MODIFY] `Assets/ARAppModeController.cs`
- Add a new static method: `Create3DButtonObject(string name, Transform parent, string labelText, out Button buttonComponent, out TMP_Text textComponent)`.
- **Hierarchy Structure**:
  - `Base Object`: Black background, representing the 3D depth and shadow. Attached to the layout group.
  - `Top Layer`: `#e8e8e8` background with an `Outline` component (black border). Contains the `Button3DAnimator`.
  - `Text`: Black bold text, centered inside the top layer.
- Update `AddButton` and all explicit calls to `CreateButtonObject` that generate text buttons (Mode buttons, Help/Instruction dismiss buttons, etc.) to use this new 3D style. *(Note: Icon buttons like Gear/Help that are transparent will retain their specific icon setup).*

#### [MODIFY] `Assets/BoneQuizController.cs`
- Replace calls to `CreateButtonObject` with `Create3DButtonObject` for the "Start Button", "Exit Button", and "Back Button" so the quiz minigame perfectly matches the new aesthetic.

## User Review Required
> [!IMPORTANT]
> The provided CSS forces a very specific color scheme: White/Gray (`#e8e8e8`) buttons with Black text and Black 3D shadows. 
> This means I will strip away the Purple/Red colors you currently have on your buttons (like the purple "Start" button) to exactly match your CSS snippet. Are you okay with all text buttons becoming White/Black to match this premium styling, or do you want me to keep the colored backgrounds and just apply the 3D push animation structure to them?
