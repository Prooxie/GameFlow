# GameFlow terms

GameFlow maps input devices to controller outputs through saved profiles.

| Term | Meaning |
| --- | --- |
| Physical input | A gamepad, joystick, keyboard, mouse, or connected phone. |
| Virtual output | A controller that GameFlow creates for another application. |
| Slot | One input assignment, profile list, and output template. |
| Profile | A saved JSON document with mapping rules and input settings. |
| Rule | One transformation that changes input or output. |
| Tick | One pass through the mapping pipeline. |
| Snapshot | One recorded input state or output state. |
| Theme | Artwork and geometry for a controller surface. |
| Prompt | An image that identifies a button in the mapping picker. |
| Preview | A visual output that creates no operating system controller. |
| Phone seat | One saved phone identity with a private connection link. |
| Overlay | A read-only browser view for OBS or another local browser. |
| Effect | A rumble, trigger, or lighting command for physical hardware. |

The source layers are App, Core, Infrastructure, and the HIDMaestro adapter.
The sibling HIDMaestro source is third-party reference material.
GameFlow must access that SDK through its adapter.
