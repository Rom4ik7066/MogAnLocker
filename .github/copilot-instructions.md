# Copilot instructions for MogLocker

## Build, test, and validation commands

This repository is a Windows .NET Framework 4.8 WinForms app with a browser extension companion. There is no `dotnet test` pipeline or JavaScript lint configuration here; the project is validated with the repository's batch scripts.

- Full build: `build.cmd` (from the repo root)
  - Produces `bin\MogLocker.exe`, packages the bundled launcher, and zips the browser extension into the final artifact.
- Solution build (if you need MSBuild directly): `msbuild MogLocker.sln /p:Configuration=Debug`
- Smoke test suite: `tests\run-smoke-tests.cmd`
  - This compiles the main application and runs the single smoke-test executable defined in `tests\AdBlockEngineSmokeTests.cs`.
  - It validates major AdBlock behaviors: filter parse/compile, allowlist handling, user rule validation, and persistence.
- There is no per-test runner for individual test methods; the project uses a single smoke executable instead of a full xUnit/NUnit setup.
- No dedicated lint target exists in this repo. Treat `build.cmd` and the smoke tests as the main validation gates.

## High-level architecture

The codebase is effectively two coupled products in one repo:

1. Windows desktop app (`MogLocker.cs`, `AnimatedButton.cs`, and the `AdBlock/` folder)
   - `MogLocker.cs` owns the WinForms UI, monitoring loops, hotkeys, registry and filesystem checks, and alert aggregation.
   - `AdBlock` contains the rule engine and browser integration for ad-filtering:
     - `AdBlockStore` persists settings in `%LOCALAPPDATA%\MogLocker\AdBlock\settings.json` and uses a mutex for safe atomic writes.
     - `FilterRuleEngine` parses a supported subset of ABP syntax and emits the browser's declarative rule model for network and cosmetic filtering.
     - `NativeMessagingHost` and `NativeMessagingRegistration` handle Chrome/Edge Native Messaging registration and message plumbing.
     - `AdBlockPanel` provides the in-app UI for filter lists, allowlist, custom rules, and runtime statistics.
2. Browser extension (`browser-extension/`)
   - `manifest.json` defines the MV3 extension and Chrome/Edge registration metadata.
   - `background.js` installs `declarativeNetRequest` rules, applies cosmetic CSS, and reports matches back to the desktop app.
   - `popup.html`, `popup.css`, and `popup.js` provide the compact extension popup.

The flow is: desktop app manages monitoring and AdBlock state; the browser extension enforces rules in the browser; the OS-level Native Messaging bridge lets the browser share actual block events and allowlist decisions back to the app.

## Key conventions and repository-specific patterns

- Target framework is intentionally .NET Framework 4.8; do not introduce .NET Core/modern SDK-only patterns or packages unless the project has already been explicitly moved off the .NET Framework.
- The shipped app artifact is `bin\MogLocker.exe`; this is a bundled launcher/packaged executable rather than a straightforward source build output. Changes to `Launcher.cs`, `bootstrap.ps1`, or `build-extension.ps1` affect the final distribution artifact.
- The AdBlock engine is intentionally conservative. `FilterRuleEngine` caps dynamic rule counts and rejects unsupported ABP constructs instead of silently approximating them. Preserve that behavior when changing rule compilation logic.
- AdBlock configuration is stored under a single JSON state file with version/revision fields; keep writes atomic and mutex-protected when changing state or cache behavior.
- Browser-extension and desktop app rule semantics must stay synchronized. If you change filter syntax handling or manifest behavior, check both the app-side `AdBlock` code and the extension-side JS.
- The project uses `#region`-style organized WinForms code and direct Win32 interop (`user32.dll`, registry access, hotkeys) in `MogLocker.cs`; keep native API calls explicit and Windows-specific.
- Keep compatibility for local Windows installation paths and app-data layout. The README documents real-world installation expectations around `%LOCALAPPDATA%`, Start menu shortcuts, and Edge/Chrome extension loading.

## Working in this repo

- Prefer surgical changes in the relevant subsystem: WinForms monitor logic, AdBlock engine, or browser extension, rather than broad refactors.
- When changing AdBlock rules or storage semantics, validate with `tests\run-smoke-tests.cmd` before considering the patch ready.
- If you need to inspect the shipped behavior, the README is the best source for the product flow and installation story; the code is Windows-specific and not cross-platform.
