# Password Generator

A local WPF app for generating cryptographically random passwords on Windows.

For scripted or automated password generation (CI pipelines, other local tooling), see `New-CryptoPassword.ps1` instead - it implements the same algorithm in PowerShell with no compiled artifact required.

## Features

- **Random characters mode**: length 4-128, independently toggleable uppercase/lowercase/digits/symbols, optional exclusion of visually ambiguous characters (l, 1, I, O, 0). Every selected character type is guaranteed to appear at least once.
- All randomness comes from `System.Security.Cryptography.RandomNumberGenerator` - a CSPRNG, not a general-purpose PRNG - for both selection and shuffling.
- Estimated entropy (in bits) shown alongside the result.
- One-click copy to clipboard.

## Requirements

- .NET 10 SDK
- Windows (WPF only runs on Windows)

## Building and running

```
dotnet build
dotnet run
```

## Project layout

| File | Purpose |
|---|---|
| `CryptoPasswordGenerator.cs` | Random-character generation logic (`PasswordOptions`, `Generate`, `EstimateEntropyBits`). No UI dependency. |
| `MainWindow.xaml` / `.xaml.cs` | UI layout and event wiring: mode toggle, length/word-count controls, character-type, generate/copy actions. |
| `App.xaml` | Merges the design-system resource dictionaries application-wide. |
| `Resources/Styles.*.xaml` | Implicit styles for Button, CheckBox, RadioButton, Slider, TextBox, plus keyed card and decision-label styles, adapted from another project's design system. |
| `password_generator.ico` | Application/window icon (multi-resolution, transparent background). |

## Notes

- The character-class guarantee ("every selected type appears at least once") is unconditional by design - there's no scenario where selecting a character type but excluding it from the result would be useful, so it isn't exposed as a toggle.

