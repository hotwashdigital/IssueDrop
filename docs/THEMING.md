# IssueDrop theme contract

IssueDrop control markup uses semantic resources rather than palette colors. A theme is one WPF `ResourceDictionary` under `src/IssueDrop/Themes` that defines every key below. `ThemeManager` swaps the active dictionary at runtime, so every usage must remain a `DynamicResource`.

| Token | Purpose |
|---|---|
| `TransparentBrush` | Transparent layout and hit-test surfaces |
| `WindowBackgroundBrush` | Primary window and popup background |
| `SurfaceBrush` | Grouped cards and attachment chips |
| `ElevatedSurfaceBrush` | Hovered or visually raised surfaces |
| `InputBackgroundBrush` | Text inputs and selection controls |
| `TextPrimaryBrush` | Default readable text on window and surface backgrounds |
| `TextSecondaryBrush` | Supporting labels, metadata, and placeholders |
| `TextDisabledBrush` | Disabled or unavailable content |
| `BorderBrush` | Dividers, outlines, and input borders |
| `AccentBrush` | Primary actions and active accents |
| `AccentHoverBrush` | Hover state for primary actions |
| `TextOnAccentBrush` | Text and glyphs shown on the accent color |
| `DangerBrush` / `DangerSurfaceBrush` | Error content and its background |
| `SuccessBrush` / `SuccessSurfaceBrush` | Success content and its background |
| `OverlayBrush` | Modal busy overlay |
| `SelectionBrush` / `TextOnSelectionBrush` | Selected text and selected rows |
| `CodeBackgroundBrush` | Markdown preview code blocks |
| `ScrollBarTrackBrush` / `ScrollBarThumbBrush` | Scroll controls |
| `ShadowColor` | Window and popup shadows (`Color`, not a brush) |

To add a theme, copy an existing dictionary, change only the token values, add the preference to `ThemePreference`, and map it in `ThemeManager.Apply`. Keep the token names identical across every dictionary. The validation suite fails if light and dark drift or if literal colors appear in application XAML outside `Themes`.

For contrast, `TextPrimaryBrush` must be readable on the window, surface, elevated, and input backgrounds; `TextOnAccentBrush` must be readable on both accent states. Avoid encoding a component name in new tokens—the same semantic state should share the same token throughout the app.
