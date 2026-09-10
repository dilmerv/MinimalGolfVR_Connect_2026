---
name: activate-xr-simulator
description: Activate, deactivate, or check the Meta XR Simulator through the Unity toolbar menu via the Unity CLI Pipeline.
---

# Activate Meta XR Simulator

Use the Unity CLI (`unity command`) to drive the Meta XR Simulator toolbar menu items. Do not edit Editor state through other automation.

## Menu paths

- Activate: `Window/Meta/Meta XR Simulator/Activate`
- Deactivate: `Window/Meta/Meta XR Simulator/Deactivate`

## Procedure

1. Confirm the Editor is reachable: `unity status` should show the project as `ready`.
2. Execute the menu item, e.g.:
   `unity command menu --path "Window/Meta/Meta XR Simulator/Activate" --project-path <project>`
   A successful run returns `success: true` with message `Executed menu item '...'`.

## Verification

- Check the console for `[Meta XR Simulator is activated]` (or `Status: Installed: Yes, Active: Yes` after running the Status item):
  `unity command console --tail 50 --project-path <project>`

## Additional Rules

- No need to check Meta XR Simulator Status, this opens a popup which I don't want to see during the activation.
