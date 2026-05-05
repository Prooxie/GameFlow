# Autofire Next

> A controller-mapping engine for Windows, Linux, and macOS — turbo, freeze-direction, stick shaping, button combos, and Lua scripting on top of any gamepad your OS can see.

---

## Table of contents

1. [What it does](#what-it-does)
2. [Install](#install)
3. [First-run tutorial](#first-run-tutorial)
4. [Mapping rule types](#mapping-rule-types)
5. [Lua scripting](#lua-scripting)
6. [Profiles & sharing](#profiles--sharing)
7. [Platform notes](#platform-notes)
8. [Build from source](#build-from-source)
9. [Release process (for maintainers)](#release-process-for-maintainers)
10. [Troubleshooting](#troubleshooting)

---

## What it does

Autofire Next reads a physical controller (XInput, SDL3, Linux evdev, or macOS CoreHID), runs your mapping profile through a deterministic pipeline, and writes the result to a virtual controller (ViGEm Xbox 360, ViGEm DualShock 4, ViGEm DualSense, or a preview buffer).

The mapping pipeline supports:

- **Button remap** — re-route any button to any other button.
- **Button autofire / turbo** — hold a button to pulse another at a configurable hold/release rate.
- **Stick threshold shaping** — deadzone + saturation curve per stick.
- **Stick autofire / pulse** — pulse the virtual stick in any direction while a real stick is held.
- **Freeze last direction** — capture the stick vector at the rising edge of an activation button and hold it until released.
- **Button combos** (joy2key-style) — one source button → an ordered sequence of virtual button presses with per-step delay and hold.
- **Lua scripts** — full programmable control over the virtual snapshot.

Five visual themes (Cyber Blue, Midnight Purple, Neon Green, Solar Red, Light), 30 Hz dashboard, every UI string is localised.

---

## Install

### Windows
1. Install the **ViGEm Bus driver** if you want virtual-controller output: <https://github.com/nefarius/ViGEmBus/releases>
2. Download `Autofire-Next-vX.Y.Z-win-x64.zip` from the [releases page](../../releases).
3. Unzip anywhere and run `Autofire.App.exe`.

### Linux
1. Add yourself to the `input` group so the app can read gamepads:
   ```bash
   sudo usermod -aG input $USER
   newgrp input
   ```
2. Download `Autofire-Next-vX.Y.Z-linux-x64.tar.gz`.
3. Extract and run `./Autofire.App`.

### macOS
1. Download `Autofire-Next-vX.Y.Z-osx-arm64.tar.gz` (Apple Silicon) or `osx-x64.tar.gz` (Intel).
2. Extract anywhere.
3. First launch will prompt for **Input Monitoring** permission — grant it in *System Settings → Privacy & Security → Input Monitoring*.

---

## First-run tutorial

When you launch Autofire Next you land on the **Dashboard** tab. Before anything else, set the four dashboard fields top-to-bottom.

### Step 1 — pick an input provider

| Provider | When to choose it |
|----------|-------------------|
| **XInput** | Windows, Xbox-class controller |
| **SDL3 unified input** | Anything else on Windows, all Linux, all macOS — broadest hardware support |
| **Demo preview** | Test the UI without a real controller |
| **No live input** | Pause the pipeline |

### Step 2 — pick an output provider

| Provider | When to choose it |
|----------|-------------------|
| **ViGEm Xbox 360** | Most Windows games — broadest game compatibility |
| **ViGEm DualShock 4** | Games that prefer PlayStation prompts |
| **ViGEm DualSense (DS5)** | Games that detect DS5 specifically (uses DS4 transport with DS5 product strings) |
| **Preview only** | Cross-platform — view the transformed output in-app, do **not** create a virtual device |

> **Linux/macOS** today only support **Preview only** as an output provider. Linux uinput output is on the roadmap.

### Step 3 — pick the controller and visual styles

Leave **Controller** on *Automatic* unless you have multiple gamepads. **Physical style** and **Virtual style** affect *only* the dashboard visualisation.

### Step 4 — set the polling rate, then click **Apply**

For most desktop gamepads, **250 Hz** is a sweet spot. For competitive setups with high-poll-rate pads, push it to **1000 Hz**.

Click **Apply**. You should see your physical controller's inputs reflected in both visualisations.

### Step 5 — add your first rule

Switch to the **Profiles** tab.

**Example: turbo Y at 10 Hz when right-bumper is held.**

1. In the *Mapping rules* combobox, choose **Button autofire**, then click **+ Add rule**.
2. In the form on the right:
   - **Rule name:** `Turbo Y`
   - **Trigger button:** `RightShoulder`
   - **Output button:** `North`
   - **Suppress source button:** ✓
   - **Hold:** `60 ms`
   - **Release:** `40 ms`
3. Click **Save rule**.
4. Click the green **Save** button in the profile toolbar to persist the profile to disk.

Hold RB. The dashboard's virtual controller will pulse Y at ~10 Hz.

### Step 6 — switch language and theme

The two header dropdowns swap UI language and colour theme without restarting. All five themes meet WCAG-compliant text contrast — text stays visible.

---

## Mapping rule types

| Rule | What it does | Use case |
|------|--------------|----------|
| Button remap | Source button → target button. | Swap A/B, route stick-click to Back. |
| Button autofire | Source held → pulse target at HoldMs / ReleaseMs. | Turbo, rapid-fire. |
| Stick threshold | Deadzone + saturation curve per stick. | Tighten loose sticks, fix drift. |
| Stick autofire | Pulse a stick direction while a real stick is held. | Auto-walk, pulse-aim. |
| Freeze last direction | Capture stick vector on the rising edge of a button, hold until release. | "Aim lock" while throwing grenades. |
| Button combo (joy2key) | Source button → ordered sequence of virtual presses with per-step delay & hold. | Fighting-game macros, jump-cancel. |
| Script | Lua code that reads physical, writes virtual, every tick. | Anything not covered above. |

Every rule has **Enabled**, **Name**, **Mode** and **Suppress source** controls plus rule-specific fields. Rules execute in list order, so later rules can read what earlier rules wrote.

---

## Lua scripting

Autofire Next embeds a sandboxed [MoonSharp](https://www.moonsharp.org/) interpreter for arbitrary scripts.

1. *Profiles* tab → **Add rule** → **Script** → save.
2. Click **Edit** on the rule.
3. Set **Target control key** (informational — the script can press anything).
4. Type your Lua in the **Script** field.

### Required entry point

```lua
function on_tick(ctx)
  -- runs once per polling tick
end
```

### Context API

| Member | Type | Description |
|--------|------|-------------|
| `ctx.left.x`, `ctx.left.y`     | number (-1..1) | Physical left-stick vector |
| `ctx.right.x`, `ctx.right.y`   | number (-1..1) | Physical right-stick vector |
| `ctx.lt`, `ctx.rt`             | number (0..1)  | Physical triggers |
| `ctx.is_pressed("South")`      | bool           | Query a physical button by name (any `ButtonId` value) |
| `ctx.press("South")`           | function       | Set the named virtual button pressed |
| `ctx.release("South")`         | function       | Set the named virtual button released |
| `ctx.set_left(x, y)`           | function       | Write the virtual left stick |
| `ctx.set_right(x, y)`          | function       | Write the virtual right stick |
| `ctx.set_lt(value)`, `ctx.set_rt(value)` | function | Write virtual triggers |
| `ctx.now_ms`                   | number         | Tick timestamp in ms since Unix epoch |
| `ctx.dt_ms`                    | number         | Ms elapsed since the previous invocation of *this* script |
| `ctx.state`                    | table          | Persistent per-script state |

Button names: `South`, `East`, `West`, `North`, `LeftShoulder`, `RightShoulder`, `LeftTriggerButton`, `RightTriggerButton`, `Back`, `Start`, `Guide`, `LeftStick`, `RightStick`, `DpadUp`, `DpadDown`, `DpadLeft`, `DpadRight`, `Touchpad`.

### Example 1 — turbo Y while LB is held

```lua
local TURBO_HZ = 10
local PERIOD_MS = 1000 / TURBO_HZ

function on_tick(ctx)
  if not ctx.is_pressed("LeftShoulder") then
    ctx.state.t = nil
    return
  end

  ctx.state.t = (ctx.state.t or 0) + ctx.dt_ms
  local phase = ctx.state.t % PERIOD_MS
  if phase < (PERIOD_MS / 2) then
    ctx.press("North")
  else
    ctx.release("North")
  end
end
```

### Example 2 — radial deadzone + sensitivity curve on the right stick

```lua
local DEADZONE = 0.12
local CURVE = 1.4   -- >1 = slower around centre, faster at the edges

function on_tick(ctx)
  local x, y = ctx.right.x, ctx.right.y
  local mag = math.sqrt(x*x + y*y)

  if mag < DEADZONE then
    ctx.set_right(0, 0)
    return
  end

  local scaled = ((mag - DEADZONE) / (1 - DEADZONE)) ^ CURVE
  local nx, ny = (x / mag) * scaled, (y / mag) * scaled
  ctx.set_right(nx, ny)
end
```

### Example 3 — fighting-game macro on RB (quarter-circle + South)

```lua
local SEQUENCE = {
  { dir = {0, -1},  ms = 60 },           -- down
  { dir = {1, -1},  ms = 60 },           -- down-forward
  { dir = {1, 0},   ms = 60 },           -- forward
  { dir = nil, btn = "South", ms = 80 }, -- punch
}

function on_tick(ctx)
  if ctx.is_pressed("RightShoulder") and not ctx.state.running then
    ctx.state.running = { started = ctx.now_ms }
  end
  if not ctx.state.running then return end

  local elapsed = ctx.now_ms - ctx.state.running.started
  local cumulative = 0
  for _, step in ipairs(SEQUENCE) do
    cumulative = cumulative + step.ms
    if elapsed < cumulative then
      if step.dir then ctx.set_left(step.dir[1], step.dir[2]) end
      if step.btn then ctx.press(step.btn) end
      return
    end
  end

  ctx.state.running = nil
end
```

### Sandbox restrictions

The Lua sandbox **disables** `os`, `io`, `debug`, `package`, `require`, `loadfile`, and `dofile`. Scripts can compute, read `ctx`, write virtual state, and use `ctx.state` for persistence — nothing else.

---

## Profiles & sharing

Profiles are JSON, stored at:

| OS | Path |
|----|------|
| Windows | `%APPDATA%\Autofire.Next\profiles\` |
| Linux   | `~/.config/Autofire.Next/profiles/` |
| macOS   | `~/Library/Application Support/Autofire.Next/profiles/` |

Use **Export** to share, **Import** to receive.

---

## Platform notes

| Platform | Input | Output |
|----------|-------|--------|
| Windows  | XInput, SDL3, Microsoft.GameInput\* | ViGEm 360 / DS4 / DS5, Preview |
| Linux    | SDL3, evdev | Preview |
| macOS    | SDL3, CoreHID | Preview |

\*GameInput is shipped **disabled** until the interop layer is hosted out-of-process — flip `Runtime:EnableExperimentalGameInput=true` in `appsettings.json` to opt in.

### PlayStation 3 controllers (Sixaxis / DS3)

DS3 controllers render with a dedicated silhouette and are detected on every platform, but full feature support requires platform-specific drivers:

- **Windows:** [DsHidMini](https://github.com/nefarius/DsHidMini) or [ScpToolkit](https://github.com/nefarius/ScpToolkit). The DS3 then enumerates as XInput.
- **Linux:** kernel drivers `hid-sony` (USB) and `hid-playstation` (Bluetooth) work on kernel 5.12+ without any user setup.
- **macOS:** USB only out of the box; Bluetooth needs a third-party kext.

---

## Build from source

```bash
git clone https://github.com/YOUR_GH_USER/Autofire-Next.git
cd Autofire-Next

dotnet restore Autofire.Next.sln
dotnet build  Autofire.Next.sln --configuration Release
dotnet test   Autofire.Next.sln --configuration Release --no-build
dotnet run --project src/Autofire.App
```

Requires .NET **10.0** SDK or newer.

---

## Release process (for maintainers)

GitHub Actions handles everything once a version tag lands:

```bash
# bump version in src/Autofire.App/Autofire.App.csproj <Version>X.Y.Z</Version>
git commit -am "chore: bump to vX.Y.Z"
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin main --tags
```

The `ci.yml` workflow then:

1. Builds & tests on Windows, Linux, macOS (Apple Silicon **and** Intel).
2. Publishes self-contained single-file binaries for `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`.
3. Creates a GitHub Release named after the tag and attaches all four artifacts.
4. Auto-generates release notes from PRs / commits since the previous tag.

Watch the workflow run at *Actions → ci → \<your tag\>*.

---

## Troubleshooting

| Symptom | Fix |
|--------|------|
| "No controllers detected" on Windows even though my pad works in games | Try the **SDL3** input provider — XInput only sees Xbox-class devices. |
| `DllNotFoundException: SDL3` on first launch | The SDL3 native library lives in `runtimes/<rid>/native/`. Reinstall and verify SmartScreen didn't quarantine it. |
| Linux: app starts but enumerates zero pads | Confirm `groups` shows `input`. `udevadm monitor` should print events when you replug a pad. |
| ViGEm output silently does nothing | ViGEm Bus driver is not installed or has been blocked by Secure Boot — reinstall from <https://github.com/nefarius/ViGEmBus/releases>. |
| Crash on application exit | Fixed in v1.0.0+. Make sure you're on the latest release. |

---

## License

MIT — see `LICENSE`.

Made by **Proxy Darkness**. Pull requests welcome.
