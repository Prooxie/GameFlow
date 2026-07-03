"""
Generator for per-variant theme.json files.

Each theme.json lives in its own subfolder so ThemeRegistry can pick
the user's chosen variant. The base template is the "most stripped"
variant (no analog stick, no triggers, no lightbar); we then overlay
the triggers + lightbar + sticks as separate elements so the active
overlays (button presses, trigger pull, stick deflection) can light
up independently of the background.

Image paths in theme.json use the `\` prefix so they resolve from
the themes/ root via ThemeSurface.LoadBitmap's ThemesRootDirectory
branch — that way every variant under a given style can share the
same image assets without duplicating them per folder.

This file is intentionally check-in-able but not loaded at runtime —
it's a one-shot generator. After editing it run `python _generate.py`
from inside the themes/ folder.
"""

from __future__ import annotations

import json
import os
from pathlib import Path

HERE = Path(__file__).parent


def write_theme(folder: Path, doc: dict) -> None:
    folder.mkdir(parents=True, exist_ok=True)
    (folder / "theme.json").write_text(
        json.dumps(doc, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )


# ────────────────────────────────────────────────────────────────────────
# DualSense variants
# ────────────────────────────────────────────────────────────────────────
#
# Each color has a "most stripped" base PNG. Some filenames are truncated
# on disk (Galactic Purple, Nova Pink, Starlight Blue) — we preserve the
# exact on-disk filenames so paths actually resolve. The unrelated colour
# variants (Lightbar PNG) are shared across all DualSense colours so they
# always come from `\dualsense\Image Assets\DualSense_Lightbar.png`.

DUALSENSE_VARIANTS = {
    # variant-folder-name : on-disk "stripped" base filename
    "Cosmic Red":        "DualSense Controller Layout VSCView - Cosmic Red (No Analog Stick, Triggers and Lightbar).png",
    "Galactic Purple":   "DualSense Controller Layout VSCView - Galactic Purple (No Analog Stick,.png",
    "Midnight Black":    "DualSense Controller Layout VSCView - Midnight Black (No Analog Stick,Triggers and Lightbar).png",
    "Nova Pink":         "DualSense Controller Layout VSCView - Nova Pink (No Analog Stick, Trigge.png",
    "Starlight Blue":    "DualSense Controller Layout VSCView - Starlight Blue (N.png",
    "White":             "DualSense Controller Layout VSCView (No Analog Stick, Triggers and Lightbar).png",
}


def dualsense_theme(color: str, base_png: str) -> dict:
    return {
        "_comment": (
            f"DualSense {color}. Base is the 'no analog stick, no triggers, "
            f"no lightbar' template; triggers, lightbar and analog sticks are "
            f"applied as overlays so each can animate independently."
        ),
        "name": f"DualSense ({color})",
        "version": 1,
        "width": 1467,
        "height": 816,

        "children": [
            # Base controller body (with everything stripped).
            { "type": "image", "x": 0, "y": 0,
              "image": f"\\dualsense\\Templates\\{color}\\{base_png}",
              "width": 1467, "height": 816 },

            # Lightbar removed — the real device's lightbar colour and
            # state will be mirrored in a separate render pass in the
            # next patch (reading the controller's actual LED output)
            # rather than baked as a static PNG overlay here.

            # L2 / R2 trigger bodies (re-added on top of the stripped base).
            { "type": "image", "x": 170, "y": 1,
              "image": "\\dualsense\\Image Assets\\Button Colors\\DualSense_L2.png",
              "width": 201, "height": 152 },
            { "type": "image", "x": 1098, "y": 0,
              "image": "\\dualsense\\Image Assets\\Button Colors\\DualSense_R2.png",
              "width": 197, "height": 149 },

            # Trigger pull (pbar fills from bottom upwards as triggers pull).
            { "type": "pbar", "x": 170, "y": 1,
              "input": "triggers:l:analog", "direction": "up",
              "image": "\\dualsense\\Image Assets\\DualSense_L2-Active.png",
              "width": 201, "height": 152 },
            { "type": "pbar", "x": 1098, "y": 0,
              "input": "triggers:r:analog", "direction": "up",
              "image": "\\dualsense\\Image Assets\\DualSense_R2-Active.png",
              "width": 197, "height": 149 },

            # L1 / R1 bumpers (press overlays).
            { "type": "showhide", "input": "bumpers:l", "children": [
                { "type": "image", "x": 156, "y": 85,
                  "image": "\\dualsense\\Image Assets\\DualSense_L1-Active.png",
                  "width": 210, "height": 124 } ] },
            { "type": "showhide", "input": "bumpers:r", "children": [
                { "type": "image", "x": 1100, "y": 85,
                  "image": "\\dualsense\\Image Assets\\DualSense_R1-Active.png",
                  "width": 210, "height": 124 } ] },

            # Menu buttons.
            { "type": "showhide", "input": "home", "children": [
                { "type": "image", "x": 690, "y": 547,
                  "image": "\\dualsense\\Image Assets\\DualSense_Home_Button.png",
                  "width": 93, "height": 51 } ] },
            { "type": "showhide", "input": "menu:l", "children": [
                { "type": "image", "x": 354, "y": 258,
                  "image": "\\dualsense\\Image Assets\\DualSense_Create_Button.png",
                  "width": 47, "height": 63 } ] },
            { "type": "showhide", "input": "menu:r", "children": [
                { "type": "image", "x": 1068, "y": 258,
                  "image": "\\dualsense\\Image Assets\\DualSense_Option_Button.png",
                  "width": 47, "height": 63 } ] },
            { "type": "showhide", "input": "home:mute", "children": [
                { "type": "image", "x": 698, "y": 660,
                  "image": "\\dualsense\\Image Assets\\DualSense_Mute_Button.png",
                  "width": 77, "height": 36 } ] },

            # Face buttons (Cross/Circle/Square/Triangle).
            { "type": "showhide", "input": "quad_right:s", "children": [
                { "type": "image", "x": 1154, "y": 478,
                  "image": "\\dualsense\\Image Assets\\DualSense_Cross.png",
                  "width": 97, "height": 84 } ] },
            { "type": "showhide", "input": "quad_right:e", "children": [
                { "type": "image", "x": 1268, "y": 380,
                  "image": "\\dualsense\\Image Assets\\DualSense_Circle.png",
                  "width": 98, "height": 93 } ] },
            { "type": "showhide", "input": "quad_right:w", "children": [
                { "type": "image", "x": 1046, "y": 386,
                  "image": "\\dualsense\\Image Assets\\DualSense_Square.png",
                  "width": 99, "height": 89 } ] },
            { "type": "showhide", "input": "quad_right:n", "children": [
                { "type": "image", "x": 1159, "y": 289,
                  "image": "\\dualsense\\Image Assets\\DualSense_Triangle.png",
                  "width": 100, "height": 95 } ] },

            # D-Pad.
            { "type": "showhide", "input": "quad_left:n", "children": [
                { "type": "image", "x": 217, "y": 320,
                  "image": "\\dualsense\\Image Assets\\DualSense_D-PAD_Up.png",
                  "width": 86, "height": 98 } ] },
            { "type": "showhide", "input": "quad_left:s", "children": [
                { "type": "image", "x": 218, "y": 441,
                  "image": "\\dualsense\\Image Assets\\DualSense_D-PAD_Down.png",
                  "width": 86, "height": 95 } ] },
            { "type": "showhide", "input": "quad_left:w", "children": [
                { "type": "image", "x": 135, "y": 390,
                  "image": "\\dualsense\\Image Assets\\DualSense_D-PAD_Left.png",
                  "width": 104, "height": 80 } ] },
            { "type": "showhide", "input": "quad_left:e", "children": [
                { "type": "image", "x": 282, "y": 390,
                  "image": "\\dualsense\\Image Assets\\DualSense_D-PAD_Right.png",
                  "width": 105, "height": 80 } ] },

            # Analog sticks (slider element nudges the well-art per axis).
            { "type": "slider", "x": 406, "y": 560,
              "inputX": "stick_left:x * 25", "inputY": "stick_left:y * 25",
              "children": [
                  { "type": "image", "x": 0, "y": 0,
                    "image": "\\dualsense\\Image Assets\\Button Colors\\DualSense_LeftAnalogStick.png",
                    "width": 175, "height": 148 },
                  { "type": "showhide", "input": "stick_left:click", "children": [
                      { "type": "image", "x": 0, "y": 0,
                        "image": "\\dualsense\\Image Assets\\DualSense_AnalogStick_Click.png",
                        "width": 175, "height": 148 } ] }
              ] },
            { "type": "slider", "x": 888, "y": 561,
              "inputX": "stick_right:x * 25", "inputY": "stick_right:y * 25",
              "children": [
                  { "type": "image", "x": 0, "y": 0,
                    "image": "\\dualsense\\Image Assets\\Button Colors\\DualSense_RightAnalogStick.png",
                    "width": 175, "height": 148 },
                  { "type": "showhide", "input": "stick_right:click", "children": [
                      { "type": "image", "x": 0, "y": 0,
                        "image": "\\dualsense\\Image Assets\\DualSense_AnalogStick_Click.png",
                        "width": 175, "height": 148 } ] }
              ] },

            # Touchpad click overlay.
            { "type": "showhide", "input": "touch_center:click", "children": [
                { "type": "image", "x": 423, "y": 160,
                  "image": "\\dualsense\\Image Assets\\DualSense_Touchpad-Click.png",
                  "width": 621, "height": 322 } ] },
        ],
    }


for color, base_png in DUALSENSE_VARIANTS.items():
    write_theme(HERE / "dualsense" / color, dualsense_theme(color, base_png))


# ────────────────────────────────────────────────────────────────────────
# DualShock 4 variants
# ────────────────────────────────────────────────────────────────────────
#
# DS4 has two physical revisions: V1 (no front lightbar) and V2 (has
# front lightbar). Each ships in multiple colours. We use the "no
# thumbstick, no triggers, no lightbar rear" template as base so we
# can overlay triggers, lightbar and analog sticks independently.

DS4_V2_VARIANTS = {
    # variant key (folder name) -> { base_subfolder, base_filename, has_front_lightbar }
    "DS4 V2 Jet Black": {
        "subfolder": "Jet Black",
        "base":      "DualShock 4 Controller V2 Model Overlay - No Thumbstick, Triggers and Lightbar Rear.png",
    },
    "DS4 V2 Glacier White": {
        "subfolder": "Glacier White",
        "base":      "DualShock 4 V2 Glacier White Overlay (No Thumbstick, Triggers and Lightbar Rear).png",
    },
    "DS4 V2 Magma Red": {
        "subfolder": "Magma Red",
        "base":      "DualShock 4 V2 Magma Red Overlay (No Thumbstick, Triggers and Lightbar Rear).png",
    },
    "DS4 V2 Midnight Blue": {
        "subfolder": "Midnight Blue",
        "base":      "DualShock 4 V2 Midnight Blue Overlay (No Thumbstick, Triggers and Lightbar Rear).png",
    },
    "DS4 V2 Gold": {
        "subfolder": "Gold",
        "base":      "DualShock 4 V2 Gold Overlay (No Thumbstick, Triggers and Lightbar Rear).png",
    },
}

def ds4_v2_theme(variant_name: str, subfolder: str, base_png: str) -> dict:
    return {
        "_comment": (
            f"DualShock 4 V2 ({variant_name}). Base is the 'no thumbstick, "
            f"no triggers, no rear lightbar' overlay; triggers, lightbar and "
            f"sticks are applied as overlays."
        ),
        "name": f"DualShock 4 ({variant_name})",
        "version": 1,
        "width": 1466,
        "height": 783,

        "children": [
            # Base controller body (stripped).
            { "type": "image", "x": 0, "y": 0,
              "image": f"\\ds4\\Templates\\DS4 V2\\{subfolder}\\{base_png}",
              "width": 1466, "height": 783 },

            # Lightbar overlays removed — see DualSense comment. The
            # actual device LED state will be mirrored in a separate
            # render pass in the next patch rather than baked here.

            # L2 / R2 trigger bodies (re-added) + active pbar pull.
            { "type": "image", "x": 217, "y": 0,
              "image": "\\ds4\\Theme Assets\\DS4 V2 Active Button\\DS4_V2_L2.png",
              "width": 164, "height": 94 },
            { "type": "image", "x": 1085, "y": 0,
              "image": "\\ds4\\Theme Assets\\DS4 V2 Active Button\\DS4_V2_R2.png",
              "width": 164, "height": 94 },

            { "type": "pbar", "x": 217, "y": 0, "input": "triggers:l:analog", "direction": "up",
              "image": "\\ds4\\Theme Assets\\DS4_L2-Active.png", "width": 164, "height": 94 },
            { "type": "pbar", "x": 1085, "y": 0, "input": "triggers:r:analog", "direction": "up",
              "image": "\\ds4\\Theme Assets\\DS4_R2-Active.png", "width": 164, "height": 94 },

            # Bumpers.
            { "type": "showhide", "input": "bumpers:l", "children": [
                { "type": "image", "x": 190, "y": 71, "image": "\\ds4\\Theme Assets\\DS4_L1-Active.png", "width": 199, "height": 99 } ] },
            { "type": "showhide", "input": "bumpers:r", "children": [
                { "type": "image", "x": 1076, "y": 70, "image": "\\ds4\\Theme Assets\\DS4_R1-Active.png", "width": 199, "height": 99 } ] },

            # Menu buttons.
            { "type": "showhide", "input": "home", "children": [
                { "type": "image", "x": 688, "y": 519, "image": "\\ds4\\Theme Assets\\DS4_Home_Button.png", "width": 87, "height": 60 } ] },
            { "type": "showhide", "input": "menu:l", "children": [
                { "type": "image", "x": 416, "y": 229, "image": "\\ds4\\Theme Assets\\DS4_OptionsShare_Button.png", "width": 53, "height": 85 } ] },
            { "type": "showhide", "input": "menu:r", "children": [
                { "type": "image", "x": 996, "y": 229, "image": "\\ds4\\Theme Assets\\DS4_OptionsShare_Button.png", "width": 53, "height": 85 } ] },

            # Face buttons.
            { "type": "showhide", "input": "quad_right:s", "children": [
                { "type": "image", "x": 1124, "y": 445, "image": "\\ds4\\Theme Assets\\DS4_Face_Button.png", "width": 99, "height": 90 } ] },
            { "type": "showhide", "input": "quad_right:e", "children": [
                { "type": "image", "x": 1230, "y": 355, "image": "\\ds4\\Theme Assets\\DS4_Face_Button.png", "width": 99, "height": 90 } ] },
            { "type": "showhide", "input": "quad_right:w", "children": [
                { "type": "image", "x": 1022, "y": 354, "image": "\\ds4\\Theme Assets\\DS4_Face_Button.png", "width": 99, "height": 90 } ] },
            { "type": "showhide", "input": "quad_right:n", "children": [
                { "type": "image", "x": 1124, "y": 263, "image": "\\ds4\\Theme Assets\\DS4_Face_Button.png", "width": 99, "height": 90 } ] },

            # D-Pad.
            { "type": "showhide", "input": "quad_left:n", "children": [
                { "type": "image", "x": 250, "y": 292, "image": "\\ds4\\Theme Assets\\DS4_D-PAD_Up.png", "width": 82, "height": 90 } ] },
            { "type": "showhide", "input": "quad_left:s", "children": [
                { "type": "image", "x": 250, "y": 413, "image": "\\ds4\\Theme Assets\\DS4_D-PAD_Down.png", "width": 82, "height": 95 } ] },
            { "type": "showhide", "input": "quad_left:w", "children": [
                { "type": "image", "x": 175, "y": 361, "image": "\\ds4\\Theme Assets\\DS4_D-PAD_Left.png", "width": 101, "height": 79 } ] },
            { "type": "showhide", "input": "quad_left:e", "children": [
                { "type": "image", "x": 308, "y": 361, "image": "\\ds4\\Theme Assets\\DS4_D-PAD_Right.png", "width": 101, "height": 78 } ] },

            # Analog sticks.
            { "type": "slider", "x": 427, "y": 530, "inputX": "stick_left:x * 25", "inputY": "stick_left:y * 25",
              "children": [
                  { "type": "image", "x": 0, "y": 0, "image": "\\ds4\\Theme Assets\\DS4 V2 Active Button\\DS4_V2_LeftAnalogStick.png", "width": 165, "height": 147 },
                  { "type": "showhide", "input": "stick_left:click", "children": [
                      { "type": "image", "x": 0, "y": 0, "image": "\\ds4\\Theme Assets\\DS4_AnalogStick_Click.png", "width": 165, "height": 147 } ] } ] },
            { "type": "slider", "x": 874, "y": 530, "inputX": "stick_right:x * 25", "inputY": "stick_right:y * 25",
              "children": [
                  { "type": "image", "x": 0, "y": 0, "image": "\\ds4\\Theme Assets\\DS4 V2 Active Button\\DS4_V2_RightAnalogStick.png", "width": 165, "height": 147 },
                  { "type": "showhide", "input": "stick_right:click", "children": [
                      { "type": "image", "x": 0, "y": 0, "image": "\\ds4\\Theme Assets\\DS4_AnalogStick_Click.png", "width": 165, "height": 147 } ] } ] },

            # Touchpad click overlay.
            { "type": "showhide", "input": "touch_center:click", "children": [
                { "type": "image", "x": 410, "y": 158,
                  "image": "\\ds4\\Theme Assets\\DS4-Touchpad-Cick.png",
                  "width": 645, "height": 215 } ] },
        ],
    }


for variant, spec in DS4_V2_VARIANTS.items():
    write_theme(HERE / "ds4" / variant,
                ds4_v2_theme(variant, spec["subfolder"], spec["base"]))


# DS4 V1 — single black variant for now.
write_theme(HERE / "ds4" / "DS4 V1 Black", {
    "_comment": "DualShock 4 V1 (the original black PS4 controller). V1 has no front lightbar.",
    "name": "DualShock 4 V1 (Black)",
    "version": 1,
    "width": 1466,
    "height": 783,
    "children": [
        { "type": "image", "x": 0, "y": 0,
          "image": "\\ds4\\Templates\\DS4 V1\\DualShock 4 Controller Overlay - No Thumbstick, Triggers and Lightbar Rear.png",
          "width": 1466, "height": 783 },
        # Lightbar removed — handled in a future patch by mirroring
        # the real device's LED state instead of baking a PNG overlay.
        { "type": "image", "x": 217, "y": 0,
          "image": "\\ds4\\Theme Assets\\DS4 V2 Active Button\\DS4_V2_L2.png", "width": 164, "height": 94 },
        { "type": "image", "x": 1085, "y": 0,
          "image": "\\ds4\\Theme Assets\\DS4 V2 Active Button\\DS4_V2_R2.png", "width": 164, "height": 94 },
        { "type": "pbar", "x": 217, "y": 0, "input": "triggers:l:analog", "direction": "up",
          "image": "\\ds4\\Theme Assets\\DS4_L2-Active.png", "width": 164, "height": 94 },
        { "type": "pbar", "x": 1085, "y": 0, "input": "triggers:r:analog", "direction": "up",
          "image": "\\ds4\\Theme Assets\\DS4_R2-Active.png", "width": 164, "height": 94 },
        { "type": "showhide", "input": "bumpers:l", "children": [
            { "type": "image", "x": 190, "y": 71, "image": "\\ds4\\Theme Assets\\DS4_L1-Active.png", "width": 199, "height": 99 } ] },
        { "type": "showhide", "input": "bumpers:r", "children": [
            { "type": "image", "x": 1076, "y": 70, "image": "\\ds4\\Theme Assets\\DS4_R1-Active.png", "width": 199, "height": 99 } ] },
        { "type": "showhide", "input": "home", "children": [
            { "type": "image", "x": 688, "y": 519, "image": "\\ds4\\Theme Assets\\DS4_Home_Button.png", "width": 87, "height": 60 } ] },
        { "type": "showhide", "input": "menu:l", "children": [
            { "type": "image", "x": 416, "y": 229, "image": "\\ds4\\Theme Assets\\DS4_OptionsShare_Button.png", "width": 53, "height": 85 } ] },
        { "type": "showhide", "input": "menu:r", "children": [
            { "type": "image", "x": 996, "y": 229, "image": "\\ds4\\Theme Assets\\DS4_OptionsShare_Button.png", "width": 53, "height": 85 } ] },
        { "type": "showhide", "input": "quad_right:s", "children": [
            { "type": "image", "x": 1124, "y": 445, "image": "\\ds4\\Theme Assets\\DS4_Face_Button.png", "width": 99, "height": 90 } ] },
        { "type": "showhide", "input": "quad_right:e", "children": [
            { "type": "image", "x": 1230, "y": 355, "image": "\\ds4\\Theme Assets\\DS4_Face_Button.png", "width": 99, "height": 90 } ] },
        { "type": "showhide", "input": "quad_right:w", "children": [
            { "type": "image", "x": 1022, "y": 354, "image": "\\ds4\\Theme Assets\\DS4_Face_Button.png", "width": 99, "height": 90 } ] },
        { "type": "showhide", "input": "quad_right:n", "children": [
            { "type": "image", "x": 1124, "y": 263, "image": "\\ds4\\Theme Assets\\DS4_Face_Button.png", "width": 99, "height": 90 } ] },
        { "type": "showhide", "input": "quad_left:n", "children": [
            { "type": "image", "x": 250, "y": 292, "image": "\\ds4\\Theme Assets\\DS4_D-PAD_Up.png", "width": 82, "height": 90 } ] },
        { "type": "showhide", "input": "quad_left:s", "children": [
            { "type": "image", "x": 250, "y": 413, "image": "\\ds4\\Theme Assets\\DS4_D-PAD_Down.png", "width": 82, "height": 95 } ] },
        { "type": "showhide", "input": "quad_left:w", "children": [
            { "type": "image", "x": 175, "y": 361, "image": "\\ds4\\Theme Assets\\DS4_D-PAD_Left.png", "width": 101, "height": 79 } ] },
        { "type": "showhide", "input": "quad_left:e", "children": [
            { "type": "image", "x": 308, "y": 361, "image": "\\ds4\\Theme Assets\\DS4_D-PAD_Right.png", "width": 101, "height": 78 } ] },
        { "type": "slider", "x": 427, "y": 530, "inputX": "stick_left:x * 25", "inputY": "stick_left:y * 25",
          "children": [
              { "type": "image", "x": 0, "y": 0, "image": "\\ds4\\Theme Assets\\DS4 V2 Active Button\\DS4_V2_LeftAnalogStick.png", "width": 165, "height": 147 },
              { "type": "showhide", "input": "stick_left:click", "children": [
                  { "type": "image", "x": 0, "y": 0, "image": "\\ds4\\Theme Assets\\DS4_AnalogStick_Click.png", "width": 165, "height": 147 } ] } ] },
        { "type": "slider", "x": 874, "y": 530, "inputX": "stick_right:x * 25", "inputY": "stick_right:y * 25",
          "children": [
              { "type": "image", "x": 0, "y": 0, "image": "\\ds4\\Theme Assets\\DS4 V2 Active Button\\DS4_V2_RightAnalogStick.png", "width": 165, "height": 147 },
              { "type": "showhide", "input": "stick_right:click", "children": [
                  { "type": "image", "x": 0, "y": 0, "image": "\\ds4\\Theme Assets\\DS4_AnalogStick_Click.png", "width": 165, "height": 147 } ] } ] },
    ],
})


# ────────────────────────────────────────────────────────────────────────
# Xbox Series X variants
# ────────────────────────────────────────────────────────────────────────

XBOX_SERIES_X_COLORS = {
    # variant -> { subfolder, base_filename, stick_color_suffix }
    "Black":     { "subfolder": "Black",     "base": "Xbox Series X Controller Overlay (No Thumbstick and Triggers).png", "stick_suffix": "" },
    "Blue":      { "subfolder": "Blue",      "base": "Xbox Series X Controller Overlay - Blue (No Thumbstick and Triggers).png", "stick_suffix": "_Blue" },
    "Red":       { "subfolder": "Red",       "base": "Xbox Series X Controller Overlay - Red (No Thumbstick and Triggers).png", "stick_suffix": "_Red" },
}

def xbox_series_x_theme(color: str, subfolder: str, base_png: str, stick_suffix: str) -> dict:
    stick_left = f"\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\Color\\XBSeries_LeftStick{stick_suffix}.png"
    stick_right = f"\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\Color\\XBSeries_RightStick{stick_suffix}.png"
    return {
        "_comment": f"Xbox Series X ({color}).",
        "name":     f"Xbox Series X ({color})",
        "version":  1,
        "width":    1534,
        "height":   954,
        "children": [
            { "type": "image", "x": 0, "y": 0,
              "image": f"\\xbox-series-x\\Default\\Template\\Xbox Series X Controller\\{subfolder}\\{base_png}",
              "width": 1534, "height": 954 },

            { "type": "image", "x": 222, "y": 0,
              "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\Color\\XBSeries_LeftTrigger.png",
              "width": 166, "height": 129 },
            { "type": "image", "x": 1134, "y": 0,
              "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\Color\\XBSeries_RightTrigger.png",
              "width": 164, "height": 114 },

            { "type": "pbar", "x": 222, "y": 0, "input": "triggers:l:analog", "direction": "up",
              "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_LeftTrigger_Active.png",
              "width": 166, "height": 129 },
            { "type": "pbar", "x": 1134, "y": 0, "input": "triggers:r:analog", "direction": "up",
              "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_RightTrigger_Active.png",
              "width": 164, "height": 114 },

            { "type": "showhide", "input": "bumpers:l", "children": [
                { "type": "image", "x": 190, "y": 59,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_LeftBumper_Active.png",
                  "width": 360, "height": 124 } ] },
            { "type": "showhide", "input": "bumpers:r", "children": [
                { "type": "image", "x": 983, "y": 59,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_RightBumper_Active.png",
                  "width": 360, "height": 123 } ] },

            { "type": "showhide", "input": "home", "children": [
                { "type": "image", "x": 701, "y": 204,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_HomeButton.png",
                  "width": 132, "height": 125 } ] },
            { "type": "showhide", "input": "menu:l", "children": [
                { "type": "image", "x": 612, "y": 385,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_ViewButton.png",
                  "width": 76, "height": 70 } ] },
            { "type": "showhide", "input": "menu:r", "children": [
                { "type": "image", "x": 846, "y": 385,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_MenuButton.png",
                  "width": 76, "height": 70 } ] },
            { "type": "showhide", "input": "menu:l", "children": [
                { "type": "image", "x": 763, "y": 385,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_ShareButton.png",
                  "width": 76, "height": 70 } ] },

            { "type": "showhide", "input": "quad_right:s", "children": [
                { "type": "image", "x": 1122, "y": 461,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_A_Button.png",
                  "width": 109, "height": 106 } ] },
            { "type": "showhide", "input": "quad_right:e", "children": [
                { "type": "image", "x": 1230, "y": 353,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_B_Button.png",
                  "width": 112, "height": 107 } ] },
            { "type": "showhide", "input": "quad_right:w", "children": [
                { "type": "image", "x": 1013, "y": 364,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_X_Button.png",
                  "width": 114, "height": 108 } ] },
            { "type": "showhide", "input": "quad_right:n", "children": [
                { "type": "image", "x": 1123, "y": 256,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_Y_Button.png",
                  "width": 118, "height": 110 } ] },

            { "type": "showhide", "input": "quad_left:n", "children": [
                { "type": "image", "x": 520, "y": 562,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_D-PAD_Up.png",
                  "width": 76, "height": 69 } ] },
            { "type": "showhide", "input": "quad_left:s", "children": [
                { "type": "image", "x": 524, "y": 689,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_D-PAD_Down.png",
                  "width": 69, "height": 69 } ] },
            { "type": "showhide", "input": "quad_left:w", "children": [
                { "type": "image", "x": 443, "y": 628,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_D-PAD_Left.png",
                  "width": 80, "height": 63 } ] },
            { "type": "showhide", "input": "quad_left:e", "children": [
                { "type": "image", "x": 593, "y": 628,
                  "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_D-PAD_Right.png",
                  "width": 80, "height": 63 } ] },

            { "type": "slider", "x": 280, "y": 461, "inputX": "stick_left:x * 30", "inputY": "stick_left:y * 30",
              "children": [
                  { "type": "image", "x": 0, "y": 0, "image": stick_left, "width": 156, "height": 152 },
                  { "type": "showhide", "input": "stick_left:click", "children": [
                      { "type": "image", "x": 0, "y": 0,
                        "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_LeftStick_Click.png",
                        "width": 156, "height": 152 } ] } ] },
            { "type": "slider", "x": 802, "y": 590, "inputX": "stick_right:x * 30", "inputY": "stick_right:y * 30",
              "children": [
                  { "type": "image", "x": 0, "y": 0, "image": stick_right, "width": 156, "height": 152 },
                  { "type": "showhide", "input": "stick_right:click", "children": [
                      { "type": "image", "x": 0, "y": 0,
                        "image": "\\xbox-series-x\\Default\\Theme Assets\\Xbox Series X Active Presses\\XBSeries_RightStick_Click.png",
                        "width": 156, "height": 152 } ] } ] },
        ],
    }


for color, spec in XBOX_SERIES_X_COLORS.items():
    write_theme(HERE / "xbox-series-x" / "Default" / color,
                xbox_series_x_theme(color, spec["subfolder"], spec["base"], spec["stick_suffix"]))


# Xbox Series X — DMC5 (Devil May Cry 5) theme
write_theme(HERE / "xbox-series-x" / "DMC5", {
    "_comment": "Xbox controller in Devil May Cry 5 livery (uses the DMC5 asset set instead of stock controller PNGs).",
    "name":     "Xbox Series X (DMC5)",
    "version":  1,
    "width":    1534,
    "height":   954,
    "children": [
        { "type": "image", "x": 0, "y": 0,
          "image": "\\xbox-series-x\\DMC5\\Templates\\dmc5_xbxox_base.png",
          "width": 1534, "height": 954 },

        { "type": "image", "x": 222, "y": 0,
          "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_left_triggers.png",
          "width": 166, "height": 129 },
        { "type": "image", "x": 1134, "y": 0,
          "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_right_triggers.png",
          "width": 164, "height": 114 },

        { "type": "showhide", "input": "bumpers:l", "children": [
            { "type": "image", "x": 190, "y": 59,
              "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_left_bumper.png",
              "width": 360, "height": 124 } ] },
        { "type": "showhide", "input": "bumpers:r", "children": [
            { "type": "image", "x": 983, "y": 59,
              "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_right_bumper.png",
              "width": 360, "height": 123 } ] },

        { "type": "showhide", "input": "menu:l", "children": [
            { "type": "image", "x": 612, "y": 385,
              "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_view.png",
              "width": 76, "height": 70 } ] },
        { "type": "showhide", "input": "menu:r", "children": [
            { "type": "image", "x": 846, "y": 385,
              "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_menu.png",
              "width": 76, "height": 70 } ] },

        { "type": "showhide", "input": "quad_right:s", "children": [
            { "type": "image", "x": 1122, "y": 461,
              "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_a.png", "width": 109, "height": 106 } ] },
        { "type": "showhide", "input": "quad_right:e", "children": [
            { "type": "image", "x": 1230, "y": 353,
              "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_b.png", "width": 112, "height": 107 } ] },
        { "type": "showhide", "input": "quad_right:w", "children": [
            { "type": "image", "x": 1013, "y": 364,
              "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_x.png", "width": 114, "height": 108 } ] },
        { "type": "showhide", "input": "quad_right:n", "children": [
            { "type": "image", "x": 1123, "y": 256,
              "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_y.png", "width": 118, "height": 110 } ] },

        { "type": "showhide", "input": "quad_left:n", "children": [
            { "type": "image", "x": 520, "y": 562, "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_dpad_up.png", "width": 76, "height": 69 } ] },
        { "type": "showhide", "input": "quad_left:s", "children": [
            { "type": "image", "x": 524, "y": 689, "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_dpad_down.png", "width": 69, "height": 69 } ] },
        { "type": "showhide", "input": "quad_left:w", "children": [
            { "type": "image", "x": 443, "y": 628, "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_dpad_left.png", "width": 80, "height": 63 } ] },
        { "type": "showhide", "input": "quad_left:e", "children": [
            { "type": "image", "x": 593, "y": 628, "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_dpad_right.png", "width": 80, "height": 63 } ] },

        { "type": "slider", "x": 280, "y": 461, "inputX": "stick_left:x * 30", "inputY": "stick_left:y * 30",
          "children": [
              { "type": "image", "x": 0, "y": 0, "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_left_joystick.png", "width": 156, "height": 152 },
              { "type": "showhide", "input": "stick_left:click", "children": [
                  { "type": "image", "x": 0, "y": 0, "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_analog_stick_active.png", "width": 156, "height": 152 } ] } ] },
        { "type": "slider", "x": 802, "y": 590, "inputX": "stick_right:x * 30", "inputY": "stick_right:y * 30",
          "children": [
              { "type": "image", "x": 0, "y": 0, "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_right_joystick.png", "width": 156, "height": 152 },
              { "type": "showhide", "input": "stick_right:click", "children": [
                  { "type": "image", "x": 0, "y": 0, "image": "\\xbox-series-x\\DMC5\\Theme Assets\\dmc5_xbox_analog_stick_active.png", "width": 156, "height": 152 } ] } ] },
    ],
})


# ────────────────────────────────────────────────────────────────────────
# Xbox 360 variants
# ────────────────────────────────────────────────────────────────────────

XBOX_360_VARIANTS = {
    "Black": {"folder": "Black", "base": "Xbox 360 Controller Overlay - Black (No Thumbstick and Triggers).png"},
    "White": {"folder": "White", "base": "Xbox 360 Controller Overlay (No Thumbstick and Triggers).png"},
}

def xbox_360_theme(color: str, folder: str, base_png: str) -> dict:
    return {
        "_comment": f"Xbox 360 ({color}).",
        "name":     f"Xbox 360 ({color})",
        "version":  1,
        "width":    1545,
        "height":   955,
        "children": [
            { "type": "image", "x": 0, "y": 0,
              "image": f"\\xbox-360\\Templates\\{folder}\\{base_png}",
              "width": 1545, "height": 955 },

            { "type": "image", "x": 280, "y": 0,
              "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\Button Color\\XB360_LeftTrigger.png",
              "width": 137, "height": 144 },
            { "type": "image", "x": 1153, "y": 2,
              "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\Button Color\\XB360_RightTrigger.png",
              "width": 137, "height": 141 },

            { "type": "pbar", "x": 280, "y": 0, "input": "triggers:l:analog", "direction": "up",
              "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_LeftTrigger_Active.png",
              "width": 137, "height": 144 },
            { "type": "pbar", "x": 1153, "y": 2, "input": "triggers:r:analog", "direction": "up",
              "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_RightTrigger_Active.png",
              "width": 137, "height": 141 },

            { "type": "showhide", "input": "bumpers:l", "children": [
                { "type": "image", "x": 138, "y": 134,
                  "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_LeftBumper_Active.png",
                  "width": 312, "height": 141 } ] },
            { "type": "showhide", "input": "bumpers:r", "children": [
                { "type": "image", "x": 1125, "y": 131,
                  "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_RightBumper_Active.png",
                  "width": 285, "height": 141 } ] },

            { "type": "showhide", "input": "home", "children": [
                { "type": "image", "x": 689, "y": 414,
                  "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_GuideButton.png",
                  "width": 171, "height": 139 } ] },
            { "type": "showhide", "input": "menu:l", "children": [
                { "type": "image", "x": 557, "y": 452,
                  "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_BackButton.png",
                  "width": 92, "height": 65 } ] },
            { "type": "showhide", "input": "menu:r", "children": [
                { "type": "image", "x": 899, "y": 452,
                  "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_StartButton.png",
                  "width": 92, "height": 65 } ] },

            { "type": "showhide", "input": "quad_right:s", "children": [
                { "type": "image", "x": 1178, "y": 528, "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_A_Button.png", "width": 127, "height": 106 } ] },
            { "type": "showhide", "input": "quad_right:e", "children": [
                { "type": "image", "x": 1312, "y": 415, "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_B_Button.png", "width": 122, "height": 115 } ] },
            { "type": "showhide", "input": "quad_right:w", "children": [
                { "type": "image", "x": 1058, "y": 423, "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_X_Button.png", "width": 126, "height": 113 } ] },
            { "type": "showhide", "input": "quad_right:n", "children": [
                { "type": "image", "x": 1190, "y": 314, "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_Y_Button.png", "width": 129, "height": 118 } ] },

            { "type": "showhide", "input": "quad_left:n", "children": [
                { "type": "image", "x": 482, "y": 610, "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_D-PAD_Up.png", "width": 110, "height": 122 } ] },
            { "type": "showhide", "input": "quad_left:s", "children": [
                { "type": "image", "x": 482, "y": 720, "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_D-PAD_Down.png", "width": 110, "height": 112 } ] },
            { "type": "showhide", "input": "quad_left:w", "children": [
                { "type": "image", "x": 410, "y": 672, "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_D-PAD_Left.png", "width": 134, "height": 105 } ] },
            { "type": "showhide", "input": "quad_left:e", "children": [
                { "type": "image", "x": 530, "y": 672, "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_D-PAD_Right.png", "width": 135, "height": 105 } ] },

            { "type": "slider", "x": 204, "y": 448, "inputX": "stick_left:x * 30", "inputY": "stick_left:y * 30",
              "children": [
                  { "type": "image", "x": 0, "y": 0,
                    "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\Button Color\\XB360_LeftStick.png",
                    "width": 178, "height": 178 },
                  { "type": "showhide", "input": "stick_left:click", "children": [
                      { "type": "image", "x": 0, "y": 0,
                        "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_LeftStick_Click.png",
                        "width": 178, "height": 178 } ] } ] },
            { "type": "slider", "x": 760, "y": 608, "inputX": "stick_right:x * 30", "inputY": "stick_right:y * 30",
              "children": [
                  { "type": "image", "x": 0, "y": 0,
                    "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\Button Color\\XB360_RightStick.png",
                    "width": 178, "height": 178 },
                  { "type": "showhide", "input": "stick_right:click", "children": [
                      { "type": "image", "x": 0, "y": 0,
                        "image": "\\xbox-360\\Theme SVG\\Theme Assets\\Active Presses\\XB360_RightStick_Click.png",
                        "width": 178, "height": 178 } ] } ] },
        ],
    }


for color, spec in XBOX_360_VARIANTS.items():
    write_theme(HERE / "xbox-360" / color, xbox_360_theme(color, spec["folder"], spec["base"]))


print("OK.")
