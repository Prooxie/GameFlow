namespace GameFlow.Infrastructure.Runtime.Web.Overlay;

/// <summary>
/// The OBS overlay page, embedded as a string constant — same reasoning
/// as <see cref="WebControllerAssets"/>: one self-contained document, no
/// external requests. Art is the only thing fetched, and it comes from
/// this server.
///
/// <para>
/// The page is a tree-walker and nothing more. It knows what a
/// <c>slide</c> node is; it does not know what a trigger is, and it never
/// evaluates an expression — see <see cref="OverlayProgram"/> for why
/// that split exists. Its draw cases mirror
/// <c>ThemeSurface.RenderNode</c>, which is the contract that keeps the
/// overlay and the app's own panels drawing a theme the same way.
/// </para>
///
/// <para>
/// <b>Fingers.</b> For a theme with no <c>trailpad</c> node, the page draws
/// each touch contact inside <see cref="OverlayProgram.TouchRegion"/>, the way
/// <c>ThemeSurface.DrawContactsOverTouchRegion</c> does: a smoke trail, loose
/// smoke puffs that rise, and a soft fingertip glow. The constants are the
/// ones in <c>ThemeSurface</c>, <c>TouchTrail</c> and <c>TouchParticles</c>.
/// Change both sides together.
/// </para>
///
/// <para>
/// <b>Live layout.</b> The server sends a new <c>program</c> message when the
/// slot type or the dashboard skin changes. The page then drops the values of
/// the old tree, so no old number draws on the new layout.
/// </para>
///
/// <para>
/// <b>The background is transparent on purpose</b> and must stay that
/// way: OBS composites a browser source over the scene below it, so any
/// opaque body would show up as a black box around the controller. The
/// <c>bg</c> query parameter exists to put a colour back for looking at
/// the page in an ordinary browser, where transparent-over-nothing reads
/// as "the page is broken".
/// </para>
/// </summary>
internal static class OverlayAssets
{
    internal const string OverlayPage = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>GameFlow Overlay</title>
<style>
*{margin:0;padding:0;box-sizing:border-box}
html,body{width:100%;height:100%;overflow:hidden;background:transparent}
#c{display:block;position:fixed;inset:0;width:100%;height:100%}
#msg{position:fixed;left:50%;top:50%;transform:translate(-50%,-50%);
  font:13px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;
  color:#e6edf3;background:rgba(11,15,22,.82);border:1px solid #263041;
  border-radius:8px;padding:10px 16px;text-align:center;max-width:80vw}
#msg.hide{display:none}
</style>
</head>
<body>
<canvas id="c"></canvas>
<div id="msg">Connecting…</div>
<script>
(function(){
"use strict";

var q = new URLSearchParams(location.search);
var canvas = document.getElementById("c");
var ctx = canvas.getContext("2d");
var msg = document.getElementById("msg");

// Transparency is the default because that is what OBS needs. A colour
// here is for looking at the page in a normal browser, where a
// transparent page over a white tab reads as a broken one.
var bg = q.get("bg");
if (bg) { document.body.style.background = bg.charAt(0) === "#" ? bg : "#" + bg; }
var quiet = q.get("quiet") === "1";

function say(text) {
  if (quiet || !text) { msg.className = "hide"; return; }
  msg.textContent = text;
  msg.className = "";
}

// ── State ────────────────────────────────────────────────────────────
var program = null;      // the compiled theme
var values = [];         // this frame's evaluated expressions
var pads = [];           // this frame's touch contacts, per trailpad
var light = null;        // this frame's lightbar colour
var images = [];         // Image objects, index-aligned with program.images
var dirty = false;       // a frame arrived since the last paint

// ── Touch ────────────────────────────────────────────────────────────
// Fingers for a theme with no trailpad node, drawn as smoke the way the
// app's ThemeSurface.DrawContactsOverTouchRegion draws them. The constants
// are the ones in ThemeSurface.cs, TouchTrail.cs and TouchParticles.cs.
var FINGER_COLORS = [[0x4F,0x9C,0xFF],[0xFF,0x6B,0x35],[0x4A,0xDE,0x80],[0xE8,0x79,0xF0],[0xFB,0xBF,0x24]];
var CORE = [0xFF,0xF4,0xD6];
var SMOKE = [0xB8,0xBE,0xC8];
var SMOKE_PEAK = 0.32, SMOKE_TRAIL_RISE = 12;
var TRAIL_LIFE = 0.55, TRAIL_GAP = 0.012, TRAIL_MAX = 48, TRAIL_STEP = 0.004;
var PUFF_LIFE = 0.9, PUFF_GAP = 0.04, PUFF_MAX = 120, PUFF_RISE = 0.1;

var touchRegion = null;  // touchpad rectangle in theme units; null when the theme draws its own
var touches = [];        // this frame's contacts: [x, y, finger, pressure], each 0..1
var trails = {};         // finger slot -> [{x, y, p, t}], oldest first
var puffs = [];          // {f, x, y, vx, vy, t, life, scale}
var lastEmit = {};       // finger slot -> time of its last puff
var touchAlive = false;  // something was still fading at the last paint

function nowSeconds() { return performance.now() / 1000; }

function resetTouch() { touches = []; trails = {}; puffs = []; lastEmit = {}; touchAlive = false; }

function colorIndex(finger) {
  var n = FINGER_COLORS.length;
  return ((finger % n) + n) % n;
}

function blend(a, b, t) {
  t = Math.min(1, Math.max(0, t));
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
}

function rgba(c, alpha) {
  return "rgba(" + Math.round(c[0]) + "," + Math.round(c[1]) + "," + Math.round(c[2]) + "," + alpha + ")";
}

function spawnPuff(c, now) {
  // A small random drift, wider under more pressure, plus a rise that is
  // larger than the drift, so each puff goes up — TouchParticles.Spawn.
  var angle = Math.random() * Math.PI * 2;
  var speed = (0.01 + Math.random() * 0.05) * (0.6 + 0.8 * c[3]);
  return { f: c[2], x: c[0], y: c[1], vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed - PUFF_RISE,
           t: now, life: PUFF_LIFE * (0.65 + Math.random() * 0.7), scale: 0.6 + Math.random() * 0.9 };
}

// Called per frame message. Sampling is time-gated, so a faster or slower
// socket does not change how long the trail is.
function recordTouches(now) {
  for (var i = 0; i < touches.length; i++) {
    var c = touches[i], f = c[2];
    var list = trails[f] || (trails[f] = []);
    var last = list[list.length - 1];
    if (!last || (now - last.t >= TRAIL_GAP && Math.hypot(c[0] - last.x, c[1] - last.y) >= TRAIL_STEP)) {
      list.push({ x: c[0], y: c[1], p: c[3], t: now });
      if (list.length > TRAIL_MAX) { list.splice(0, list.length - TRAIL_MAX); }
    }
    if (lastEmit[f] === undefined || now - lastEmit[f] >= PUFF_GAP) {
      lastEmit[f] = now;
      puffs.push(spawnPuff(c, now));
    }
  }
  if (puffs.length > PUFF_MAX) { puffs.splice(0, puffs.length - PUFF_MAX); }
}

// Drops what has faded. True while anything is left to draw.
function pruneTouches(now) {
  var alive = touches.length > 0;
  for (var f in trails) {
    var list = trails[f], drop = 0;
    while (drop < list.length && now - list[drop].t > TRAIL_LIFE) { drop++; }
    if (drop > 0) { list.splice(0, drop); }
    if (list.length > 0) { alive = true; }
  }
  puffs = puffs.filter(function (s) { return now - s.t < s.life; });
  return alive || puffs.length > 0;
}

// A round fill, solid at the centre and clear at the edge — the
// SoftBrush gradient in ThemeSurface.
function soft(x, y, radius, c, alpha) {
  if (radius <= 0 || alpha <= 0) { return; }
  var g = ctx.createRadialGradient(x, y, 0, x, y, radius);
  g.addColorStop(0, rgba(c, alpha));
  g.addColorStop(0.45, rgba(c, alpha * 0.45));
  g.addColorStop(1, rgba(c, 0));
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.arc(x, y, radius, 0, Math.PI * 2);
  ctx.fill();
}

// Starts in the finger's colour, turns grey and fades as it ages.
function smoke(x, y, radius, finger, age) {
  var tint = blend(FINGER_COLORS[colorIndex(finger)], SMOKE, 0.25 + 0.75 * age);
  soft(x, y, radius, tint, SMOKE_PEAK * Math.pow(1 - age, 1.5));
}

// The trail first, then the loose puffs, then the fingertips, so the
// fingertip is always on top.
function drawTouches(now) {
  var r = touchRegion;
  function px(x) { return r.x + r.width * Math.min(1, Math.max(0, x)); }
  function py(y) { return r.y + r.height * Math.min(1, Math.max(0, y)); }

  for (var f in trails) {
    var list = trails[f];
    for (var i = 0; i < list.length; i++) {
      var s = list[i], age = Math.min(1, (now - s.t) / TRAIL_LIFE);
      smoke(px(s.x), py(s.y) - SMOKE_TRAIL_RISE * age, (5 + 13 * age) * (0.7 + 0.3 * s.p), +f, age);
    }
  }

  // Not clamped to the pad: smoke that stopped at its top edge would pile up there.
  for (var j = 0; j < puffs.length; j++) {
    var pf = puffs[j], a = Math.min(1, (now - pf.t) / pf.life), dt = now - pf.t;
    smoke(r.x + r.width * (pf.x + pf.vx * dt), r.y + r.height * (pf.y + pf.vy * dt), pf.scale * (4 + 12 * a), pf.f, a);
  }

  for (var k = 0; k < touches.length; k++) {
    var t = touches[k], x = px(t[0]), y = py(t[1]);
    soft(x, y, 12 + 8 * t[3], FINGER_COLORS[colorIndex(t[2])], 0.6);
    soft(x, y, 7, CORE, 0.45);
  }
}

// ── Canvas sizing ────────────────────────────────────────────────────
// The canvas backing store is sized in device pixels so the art is not
// resampled twice; the theme's own coordinate space is then scaled to
// fit, letterboxed, so a browser source of any size shows the whole
// controller at the right aspect.
function resize() {
  var ratio = window.devicePixelRatio || 1;
  canvas.width = Math.max(1, Math.round(window.innerWidth * ratio));
  canvas.height = Math.max(1, Math.round(window.innerHeight * ratio));
  dirty = true;
}
window.addEventListener("resize", resize);
resize();

// ── Drawing ──────────────────────────────────────────────────────────
function sizeOf(node, img) {
  // 0 means "the bitmap's own size" — the theme schema's convention.
  // Resolved here rather than server-side because the server never
  // decodes the PNG.
  return [
    node.w > 0 ? node.w : (img ? img.naturalWidth : 0),
    node.h > 0 ? node.h : (img ? img.naturalHeight : 0)
  ];
}

function ready(index) {
  var img = images[index];
  return (img && img.complete && img.naturalWidth > 0) ? img : null;
}

// Lightbar masks, tinted to the live colour. Cached because the tint is
// a whole offscreen composite and the colour changes rarely — usually
// never — while this runs at the display rate.
var tintCache = {};
function tinted(img, w, h, color) {
  var key = img.src + "|" + w + "|" + h + "|" + color;
  var hit = tintCache[key];
  if (hit) { return hit; }

  var off = document.createElement("canvas");
  off.width = Math.max(1, Math.ceil(w));
  off.height = Math.max(1, Math.ceil(h));
  var g = off.getContext("2d");
  g.drawImage(img, 0, 0, off.width, off.height);
  // source-in keeps the mask's alpha and replaces its colour, which is
  // what PushOpacityMask + FillRectangle does in the app.
  g.globalCompositeOperation = "source-in";
  g.fillStyle = color;
  g.fillRect(0, 0, off.width, off.height);

  // A theme has a handful of lightbar masks and one colour at a time.
  // The bound is here so a strobing colour cannot grow this forever.
  var keys = Object.keys(tintCache);
  if (keys.length > 32) { delete tintCache[keys[0]]; }
  tintCache[key] = off;
  return off;
}

function value(index) {
  return (index >= 0 && index < values.length) ? values[index] : 0;
}

function drawNode(node) {
  ctx.save();
  ctx.translate(node.x || 0, node.y || 0);
  if (node.r) { ctx.rotate(node.r * Math.PI / 180); }

  switch (node.kind) {
    case "show":
      // The whole subtree is skipped, which is what makes an idle
      // controller cheap to draw: every press graphic lives in one of
      // these.
      if (value(node.value) === 0) { ctx.restore(); return; }
      break;

    case "slide":
      ctx.translate(value(node.valueX), value(node.valueY));
      var rot = value(node.valueR);
      if (rot) { ctx.rotate(rot * Math.PI / 180); }
      break;

    case "trail":
      // An up finger hides the marker AND its children, the way the
      // renderer's early return does.
      if (!pads[node.pad]) { ctx.restore(); return; }
      drawTrail(node);
      break;

    case "img":
      drawImage(node);
      break;

    case "light":
      drawLight(node);
      break;

    case "bar":
      drawBar(node);
      break;
  }

  var children = node.children;
  if (children) {
    for (var i = 0; i < children.length; i++) { drawNode(children[i]); }
  }
  ctx.restore();
}

function drawImage(node) {
  var img = ready(node.image);
  if (!img) { return; }
  var wh = sizeOf(node, img);
  ctx.drawImage(img, node.center ? -wh[0] / 2 : 0, node.center ? -wh[1] / 2 : 0, wh[0], wh[1]);
}

function drawLight(node) {
  if (!light) { return; }
  var img = ready(node.image);
  if (!img) { return; }
  var wh = sizeOf(node, img);
  if (wh[0] <= 0 || wh[1] <= 0) { return; }
  ctx.drawImage(
    tinted(img, wh[0], wh[1], light),
    node.center ? -wh[0] / 2 : 0,
    node.center ? -wh[1] / 2 : 0,
    wh[0], wh[1]);
}

// One marker per trailpad node — null when that node's finger is up. A
// theme showing two fingers declares two of these, each reading its own
// contact, so there is nothing to loop over here.
function drawTrail(node) {
  var at = pads[node.pad];
  if (!at) { return; }

  var img = ready(node.image);
  if (!img) { return; }
  var wh = sizeOf(node, img);

  ctx.translate(at[0], at[1]);
  ctx.drawImage(img, -wh[0] / 2, -wh[1] / 2, wh[0], wh[1]);
}

function drawBar(node) {
  var min = value(node.min);
  var max = value(node.max);
  var range = max - min;
  var ratio = range === 0 ? 0 : Math.min(1, Math.max(0, (value(node.value) - min) / range));

  var w = node.w, h = node.h;
  var dx = node.center ? -w / 2 : 0;
  var dy = node.center ? -h / 2 : 0;

  var fx = dx, fy = dy, fw = w, fh = h;
  switch (node.dir) {
    case "left":  fx = dx + w * (1 - ratio); fw = w * ratio; break;
    case "up":    fy = dy + h * (1 - ratio); fh = h * ratio; break;
    case "down":  fh = h * ratio; break;
    default:      fw = w * ratio; break;   // right
  }

  var img = ready(node.image);
  if (img) {
    // The whole image is drawn and clipped to the fill, so a
    // trigger-shaped PNG appears to fill in rather than being squashed.
    ctx.save();
    ctx.beginPath();
    ctx.rect(fx, fy, fw, fh);
    ctx.clip();
    ctx.drawImage(img, dx, dy, w, h);
    ctx.restore();
    return;
  }

  if (node.bg) { ctx.fillStyle = node.bg; ctx.fillRect(dx, dy, w, h); }
  if (node.fg && ratio > 0) { ctx.fillStyle = node.fg; ctx.fillRect(fx, fy, fw, fh); }
}

function paint() {
  requestAnimationFrame(paint);

  // The trail and the smoke move with no new frame, so keep painting while they
  // live, plus one more paint to clear the last faded marks.
  var now = nowSeconds();
  var alive = touchRegion ? pruneTouches(now) : false;
  if (alive || touchAlive) { dirty = true; }
  touchAlive = alive;

  if (!program || !dirty) { return; }
  dirty = false;

  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, canvas.width, canvas.height);

  var scale = Math.min(canvas.width / program.width, canvas.height / program.height);
  ctx.setTransform(
    scale, 0, 0, scale,
    (canvas.width - program.width * scale) / 2,
    (canvas.height - program.height * scale) / 2);

  for (var i = 0; i < program.nodes.length; i++) { drawNode(program.nodes[i]); }
  if (touchRegion) { drawTouches(now); }
}
requestAnimationFrame(paint);

// ── Socket ───────────────────────────────────────────────────────────
// Everything the app knows about which controller to show is already in
// this page's query string, so the socket reuses it verbatim.
function socketUrl() {
  var scheme = location.protocol === "https:" ? "wss:" : "ws:";
  return scheme + "//" + location.host + "/overlay/ws" + location.search;
}

var retry = null;
function connect() {
  var socket;
  try { socket = new WebSocket(socketUrl()); }
  catch (e) { schedule(); return; }

  socket.onmessage = function (event) {
    var data;
    try { data = JSON.parse(event.data); } catch (e) { return; }

    if (data.type === "program") {
      // Also the message for a live layout change. The old values index
      // the old tree, so they must not draw on this one.
      program = data;
      values = [];
      pads = [];
      touchRegion = data.touchRegion || null;
      resetTouch();
      tintCache = {};
      images = data.images.map(function (src) {
        var img = new Image();
        // A frame can arrive before the art does; drawing skips images
        // that are not ready yet, so this only needs to ask for a repaint.
        img.onload = function () { dirty = true; };
        img.src = src;
        return img;
      });
      say(null);
      dirty = true;
      return;
    }

    if (data.type === "frame") {
      values = data.v || [];
      pads = data.pads || [];
      light = data.light || null;
      touches = data.touch || [];
      if (touchRegion) { recordTouches(nowSeconds()); }
      dirty = true;
      return;
    }

    if (data.type === "error") {
      program = null;
      say(data.message || "Overlay error");
    }
  };

  socket.onclose = function () {
    // Deliberately keeps the last frame on screen. OBS reloads a source
    // on scene changes and GameFlow restarts during a stream; blanking
    // the controller for a second each time is worse than showing a
    // still one.
    say("Reconnecting…");
    schedule();
  };

  socket.onerror = function () { try { socket.close(); } catch (e) {} };
}

function schedule() {
  if (retry) { return; }
  retry = setTimeout(function () { retry = null; connect(); }, 1500);
}

connect();
})();
</script>
</body>
</html>
""";
}
