---
name: activate-xr-simulator
description: Activate, deactivate, or check the Meta XR Simulator through the Unity toolbar menu via the Unity CLI Pipeline.
---

# Activate Meta XR Simulator

Use the Unity CLI (`unity command`) to drive the Meta XR Simulator toolbar menu items. Do not edit Editor state through other automation.

## Menu paths

- Activate: `Window/Meta/Meta XR Simulator/Activate`
- Deactivate: `Window/Meta/Meta XR Simulator/Deactivate`
- Status: `Window/Meta/Meta XR Simulator/Status`

## Procedure

1. Confirm the Editor is reachable: `unity status` should show the project as `ready`.
2. Ensure `com.unity.pipeline` is at `0.6.0-exp.1` or newer (`unity pipeline list`). Older versions cannot parse command lines — upgrade with `unity pipeline upgrade` (or `unity pipeline install --force`), then wait for recompile/domain reload and retry.
3. Confirm readiness: `unity command editor_status --project-path <project>` must report `status: ready` with `compiling: false`.
4. Execute the menu item, e.g.:
   `unity command menu --path "Window/Meta/Meta XR Simulator/Activate" --project-path <project>`
   A successful run returns `success: true` with message `Executed menu item '...'`.

## Verification

- Check the console for `[Meta XR Simulator is activated]` (or `Status: Installed: Yes, Active: Yes` after running the Status item):
  `unity command console --tail 50 --project-path <project>`
- Confirm zero errors: `unity command get_console_logs --severity Error --limit 100 --project-path <project>`
- Capture Game and Scene views:
  `unity command screenshot --view game --output <workspace-absolute-path>.png --width 1280 --height 720`
  `unity command screenshot --view scene --output <workspace-absolute-path>.png --width 1280 --height 720`
  Store captures under the project workspace (prefer `Temp/`). Inspect them before reporting completion, and report any console errors or unexpected visuals instead of claiming success.
