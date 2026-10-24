# UI translations

GameFlow ships English, Czech, German, Spanish, French, Italian, Polish and Russian catalogues under `src/GameFlow.App/Localization`.

In XAML, use a dynamic resource so an open view follows a language change:

```xml
<Button Content="{DynamicResource Ui.UiClose}" />
```

In app code, use `UiTextResources.Get(key)` or `UiTextResources.Format(key, arguments)`. Give complete sentences their own entries, including their placeholders. Avoid joining translated sentence fragments.

For selectable enum values, use `UiTextResources.EnumValue(value)` and keys such as `ButtonId.South`. Picker items that display translated properties should inherit `ViewModelBase`; the shared text service refreshes them when the language changes. Do not cache a translated label in a constructor or a static string.

Every key must exist in all eight catalogues. Each entry uses a single quoted `msgid` and `msgstr`; write line breaks inside a value as `\n`. Only the PO header uses continuation lines. Keep product names, key legends and profile syntax unchanged where appropriate. An intentionally identical phrase longer than 18 characters needs a preceding `#. invariant: reason` comment.

User names, imported skin names, device-reported names and script text are data. Preserve them when changing the app's language.

## Build checks

Every app build runs `build/LocalizationCheck.cs` before compilation.

| Error | Meaning |
| --- | --- |
| GFLOC001 | Duplicate key, unreadable catalogue or malformed PO entry |
| GFLOC002 | Missing, empty or stale translation |
| GFLOC003 | Format argument indices differ from English |
| GFLOC004 | Long English prose was copied without an invariant annotation |
| GFLOC005 | A recognised literal resource reference names an unknown key |
| GFLOC006 | A visible XAML text attribute contains hardcoded prose |
| GFLOC007 | An exposed enum value lacks its translation key |
| GFLOC008 | A direct C# status assignment contains untranslated text |

Enum checks cover input and rule pickers and catalogued enum groups.
Browser attributes, phone helpers, and fallback helpers are checked.
Direct assignments to common C# status properties must use resources.
The gate cannot discover every arbitrary C# string or prove translation quality.
The [quality report](quality-report-1.0.9.md) records the manual message audit.

Run `pwsh ./scripts/Test-LocalizationChecks.ps1` to exercise valid and deliberately broken catalogues in an isolated temporary directory. It leaves the fixture for diagnosis and never changes the working catalogues. CI runs these checks alongside builds and tests.
