namespace Autofire.Infrastructure.Overlay;

/// <summary>
/// Static assets for the controller overlay, embedded so the server is
/// fully self-contained (no asset files to ship). The page opens a
/// WebSocket to <c>/ws</c> and lights up an SVG gamepad from the streamed
/// controller state — usable both as an OBS browser source and as a live
/// sandbox for confirming which inputs the OS/drivers register.
/// </summary>
internal static class OverlayAssets
{
    public const string Html = """
<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<title>Autofire Overlay</title>
<style>
  :root { color-scheme: dark; }
  html, body { margin: 0; height: 100%; background: transparent; overflow: hidden;
    font-family: 'Segoe UI', system-ui, sans-serif; }
  #wrap { display: flex; flex-direction: column; align-items: center; justify-content: center;
    height: 100%; gap: 10px; }
  #status { font-size: 12px; letter-spacing: 0.5px; opacity: 0.55; color: #cbd5e1; }
  #status.live { color: #22c55e; }
  .el { fill: #2b3140; stroke: #3d4456; stroke-width: 2; transition: fill 60ms linear, opacity 60ms linear; }
  .el.on { fill: #38bdf8; stroke: #7dd3fc; }
  .lbl { fill: #cbd5e1; font-size: 13px; font-weight: 600; text-anchor: middle; pointer-events: none; }
  .ring { fill: none; stroke: #3d4456; stroke-width: 2; }
  .trigfill { fill: #38bdf8; }
  #device { font-size: 13px; opacity: 0.7; color: #e2e8f0; }
</style>
</head>
<body>
<div id='wrap'>
  <div id='device'>—</div>
  <svg id='pad' viewBox='0 0 460 300' width='460' height='300'>
    <!-- shoulders -->
    <rect id='LeftShoulder'  class='el' x='40'  y='28' width='90' height='20' rx='9' />
    <rect id='RightShoulder' class='el' x='330' y='28' width='90' height='20' rx='9' />
    <!-- triggers (outline + fill) -->
    <rect class='ring' x='40'  y='6' width='90' height='16' rx='8' />
    <rect class='ring' x='330' y='6' width='90' height='16' rx='8' />
    <rect id='LTfill' class='trigfill' x='42' y='8' width='0' height='12' rx='6' />
    <rect id='RTfill' class='trigfill' x='332' y='8' width='0' height='12' rx='6' />
    <!-- face buttons (N/W/E/S) -->
    <circle id='North' class='el' cx='372' cy='110' r='16' />
    <circle id='West'  class='el' cx='340' cy='142' r='16' />
    <circle id='East'  class='el' cx='404' cy='142' r='16' />
    <circle id='South' class='el' cx='372' cy='174' r='16' />
    <!-- dpad -->
    <rect id='DpadUp'    class='el' x='78'  y='96'  width='22' height='24' rx='4' />
    <rect id='DpadDown'  class='el' x='78'  y='144' width='22' height='24' rx='4' />
    <rect id='DpadLeft'  class='el' x='54'  y='120' width='24' height='22' rx='4' />
    <rect id='DpadRight' class='el' x='100' y='120' width='24' height='22' rx='4' />
    <!-- center -->
    <rect id='Back'  class='el' x='176' y='118' width='28' height='14' rx='7' />
    <rect id='Start' class='el' x='256' y='118' width='28' height='14' rx='7' />
    <circle id='Guide' class='el' cx='230' cy='125' r='14' />
    <!-- sticks -->
    <circle class='ring' cx='150' cy='210' r='34' />
    <circle class='ring' cx='310' cy='210' r='34' />
    <circle id='LeftStick'  class='el' cx='150' cy='210' r='20' />
    <circle id='RightStick' class='el' cx='310' cy='210' r='20' />
    <text class='lbl' x='150' y='266'>L</text>
    <text class='lbl' x='310' y='266'>R</text>
  </svg>
  <div id='status'>connecting…</div>
</div>
<script>
  const byId = (id) => document.getElementById(id);
  const status = byId('status');
  const deviceEl = byId('device');
  const STICK_RANGE = 14;
  const TRIG_MAX = 86;

  function applyState(s) {
    deviceEl.textContent = s.device || '—';
    const b = s.buttons || {};
    for (const name in b) {
      const el = byId(name);
      if (el) el.classList.toggle('on', !!b[name]);
    }
    // triggers
    const lt = byId('LTfill'), rt = byId('RTfill');
    if (lt) lt.setAttribute('width', String(Math.max(0, Math.min(1, s.lt || 0)) * TRIG_MAX));
    if (rt) rt.setAttribute('width', String(Math.max(0, Math.min(1, s.rt || 0)) * TRIG_MAX));
    // sticks (SVG y is down; snapshot y+ is up)
    moveStick('LeftStick', 150, 210, s.lx || 0, s.ly || 0);
    moveStick('RightStick', 310, 210, s.rx || 0, s.ry || 0);
  }

  function moveStick(id, cx, cy, x, y) {
    const el = byId(id);
    if (!el) return;
    el.setAttribute('cx', String(cx + x * STICK_RANGE));
    el.setAttribute('cy', String(cy - y * STICK_RANGE));
  }

  let ws;
  function connect() {
    ws = new WebSocket('ws://' + location.host + '/ws');
    ws.onopen = () => { status.textContent = 'live'; status.classList.add('live'); };
    ws.onmessage = (ev) => {
      try { applyState(JSON.parse(ev.data)); } catch (e) {}
    };
    ws.onclose = () => {
      status.textContent = 'reconnecting…'; status.classList.remove('live');
      setTimeout(connect, 1000);
    };
    ws.onerror = () => { try { ws.close(); } catch (e) {} };
  }
  connect();
</script>
</body>
</html>
""";
}
