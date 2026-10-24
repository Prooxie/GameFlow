namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>
/// The browser gamepad page, embedded as a string constant.
///
/// <para>
/// Deliberately one self-contained document — all CSS and JavaScript
/// inline, zero external requests. A phone loading this over the LAN
/// often has no working internet route at all (or is on a guest network
/// that blocks it), so any CDN reference would leave the page half-dead
/// exactly when it's needed.
/// </para>
///
/// <para>
/// The BIT table in the script is a wire contract shared with
/// <see cref="WebControllerProtocol"/> — change one and you must change
/// the other in the same commit (bit 16 is the Nintendo Capture button,
/// Misc1 on the server). Besides the input fields, every frame carries
/// <c>layout</c> ("xbox" | "ds4" | "switch") so the PC can draw matching
/// controller art; a stored "touch" choice from older pages migrates to
/// "switch". The socket URL carries <c>client</c>,
/// <c>token</c> and, when the page link has one, <c>seat</c> (0-15).
/// The server may answer with <c>{pad}</c>, <c>{name}</c>,
/// <c>{full:true}</c>, <c>{revoked:true}</c> and <c>{rumble}</c>;
/// full and revoked are terminal and stop the reconnect loop.
/// </para>
///
/// <para>
/// Layout is computed in a square unit (1/100 of the play-surface height,
/// or 1/178 of its width on squarer screens) so buttons stay round and
/// nothing overlaps from 16:9 through 21:9. Controls are anchored to the
/// left edge, right edge or centre, never stretched.
/// </para>
/// </summary>
internal static class WebControllerAssets
{
    public static string RenderControllerPage(GameFlow.Infrastructure.Localization.ILocalizationService? localization)
    {
        if (localization is null) { return ControllerPage; }
        var keys = System.Text.RegularExpressions.Regex.Matches(ControllerPage,
            @"phoneText\(""([^""]+)""|data-i18n(?:-aria)?=""([^""]+)""")
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .Distinct(StringComparer.Ordinal);
        var translations = keys.ToDictionary(key => key, key => localization[key]);
        return ControllerPage.Replace("/*PHONE_TRANSLATIONS*/{}", System.Text.Json.JsonSerializer.Serialize(translations), StringComparison.Ordinal)
            .Replace("<html lang=\"en\">", "<html lang=\"" + localization.CurrentCulture + "\">", StringComparison.Ordinal);
    }

    internal const string ControllerPage = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover">
<meta name="mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
<meta name="theme-color" content="#07090d">
<meta name="color-scheme" content="dark">
<title>GameFlow Controller</title>
<style>
*{margin:0;padding:0;box-sizing:border-box;-webkit-tap-highlight-color:transparent;-webkit-user-select:none;user-select:none;-webkit-touch-callout:none;touch-action:none}
html,body{width:100%;height:100%;overflow:hidden;overscroll-behavior:none;background:#06080c;color:#e6edf3;
  font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;-webkit-text-size-adjust:100%}
button{font:inherit;color:inherit;background:none;border:0;outline:0;cursor:pointer}
.hide{display:none!important}

#app{position:fixed;left:0;top:0;width:100%;height:100%;overflow:hidden;--u:3.6px;
  background:
    radial-gradient(55% 80% at 14% 80%,rgba(34,45,62,.55),rgba(34,45,62,0) 70%),
    radial-gradient(55% 80% at 86% 80%,rgba(34,45,62,.55),rgba(34,45,62,0) 70%),
    linear-gradient(180deg,#0b0f16 0%,#07090d 100%)}
#app.sideways{transform-origin:0 0;transform:rotate(90deg)}
#surface{position:absolute;overflow:hidden;
  left:env(safe-area-inset-left,0px);right:env(safe-area-inset-right,0px);
  top:env(safe-area-inset-top,0px);bottom:env(safe-area-inset-bottom,0px)}
#app.sideways #surface{left:env(safe-area-inset-top,0px);right:env(safe-area-inset-bottom,0px);
  top:env(safe-area-inset-right,0px);bottom:env(safe-area-inset-left,0px)}
#surface *{pointer-events:none}

/* ---- buttons ---- */
.btn,.trig,.tpad,.dplate,.darm,.dhub,.shome,.sbase{position:absolute}
.btn{display:flex;align-items:center;justify-content:center;border-radius:50%;line-height:1;
  color:#b9c4d2;font-weight:700;font-size:calc(var(--u)*4.2);
  background:radial-gradient(circle at 50% 28%,#2d3748 0%,#1b222e 58%,#151b25 100%);
  border:1px solid #333f53;
  box-shadow:0 calc(var(--u)*.8) 0 #030508,0 calc(var(--u)*1.4) calc(var(--u)*2.4) rgba(0,0,0,.5),inset 0 1px 0 rgba(255,255,255,.08)}
.btn svg{width:52%;height:52%;display:block}
.btn.on{transform:translateY(calc(var(--u)*.6));color:#1a0c02;border-color:#ffb27a;
  background:radial-gradient(circle at 50% 30%,#ffb066 0%,#ff7a1a 55%,#de5208 100%);
  box-shadow:0 calc(var(--u)*.2) 0 #030508,0 0 0 calc(var(--u)*.7) rgba(255,122,26,.3),0 0 calc(var(--u)*3.5) rgba(255,122,26,.55)}
.btn.bump{border-radius:calc(var(--u)*3);font-size:calc(var(--u)*4.4);letter-spacing:.04em}
.btn.mini{border-radius:999px;font-size:calc(var(--u)*2.3);font-weight:800;letter-spacing:.05em}
.btn.guide{font-size:calc(var(--u)*3.6);font-weight:800}
.btn.face{font-size:calc(var(--u)*7);font-weight:800;color:var(--fc);border-color:var(--fb);
  background:radial-gradient(circle at 50% 28%,#283141 0%,#181e29 60%,#12171f 100%)}
.btn.face svg{width:46%;height:46%}
.btn.face.on{background:var(--fc);color:#0a0d12;border-color:#fff;
  box-shadow:0 calc(var(--u)*.2) 0 #030508,0 0 0 calc(var(--u)*.7) rgba(255,122,26,.6),0 0 calc(var(--u)*4) var(--fc)}
.btn.cap{border-radius:26%}
.btn.cap svg{width:46%;height:46%}

/* Nintendo layout: neutral black and grey instead of the blue-grey shell */
.lay-switch .btn:not(.on){background:radial-gradient(circle at 50% 28%,#36383d 0%,#1e1f23 58%,#151619 100%);border-color:#46494f;color:#d6d9de}
.lay-switch .btn.face:not(.on){background:radial-gradient(circle at 50% 28%,#2c2e32 0%,#141518 62%,#0b0c0e 100%);border-color:#50535a;color:var(--fc)}
.lay-switch .trig{background:linear-gradient(180deg,#303237,#191a1d);border-color:#46494f}
.lay-switch .darm:not(.on),.lay-switch .dhub{background:linear-gradient(180deg,#303237,#191a1d)}
.lay-switch .darm:not(.on){border-color:#46494f;color:#9a9ea6}
.lay-switch .sbase:not(.act){border-color:#3b3e44}

/* ---- triggers ---- */
.trig{overflow:hidden;border:1px solid #333f53;background:linear-gradient(180deg,#232c3a,#161c26);
  box-shadow:0 calc(var(--u)*.8) 0 #030508,inset 0 1px 0 rgba(255,255,255,.07)}
.trig::after{content:"";position:absolute;left:22%;right:22%;bottom:calc(var(--u)*1.5);height:calc(var(--u)*.6);border-radius:9px;background:rgba(255,255,255,.09)}
.trig .tf{position:absolute;left:0;top:0;width:100%;height:100%;transform-origin:50% 100%;transform:scaleY(0);
  background:linear-gradient(0deg,#e0560a,#ff9a4d);will-change:transform}
.trig .tl{position:absolute;left:0;top:0;width:100%;height:100%;display:flex;align-items:center;justify-content:center;
  gap:calc(var(--u)*1.5);font-weight:800;font-size:calc(var(--u)*4.6);color:#d6dee8;text-shadow:0 1px 2px rgba(0,0,0,.6)}
.trig .tp{font-size:calc(var(--u)*2.8);font-weight:700}
.trig.on{border-color:#ffb27a;box-shadow:0 calc(var(--u)*.3) 0 #030508,0 0 calc(var(--u)*3) rgba(255,122,26,.45)}

/* ---- d-pad ---- */
.dplate{border-radius:50%;background:radial-gradient(circle,#0c1017 0%,#111722 70%,#161d29 100%);border:1px solid #1f2836;
  box-shadow:inset 0 calc(var(--u)*.8) calc(var(--u)*2.5) rgba(0,0,0,.7)}
.darm,.dhub{background:linear-gradient(180deg,#2a3444,#1a212c)}
.darm{display:flex;align-items:center;justify-content:center;color:#7d8a9c;border:1px solid #333f53;box-shadow:0 calc(var(--u)*.8) 0 #030508}
.darm svg{width:calc(var(--u)*4.4);height:calc(var(--u)*4.4);display:block}
.darm.on{background:linear-gradient(180deg,#ffa04d,#e8590c);border-color:#ffb27a;color:#1a0c02;box-shadow:0 0 calc(var(--u)*3) rgba(255,122,26,.6)}

/* ---- sticks ---- */
.shome{border-radius:50%;border:1px dashed rgba(150,165,185,.16)}
.sbase{left:0;top:0;border-radius:50%;will-change:transform;transition:transform .14s ease-out;
  background:radial-gradient(circle,#0b0f15 0%,#121822 60%,#1a2230 100%);border:1px solid #2c3647;
  box-shadow:inset 0 calc(var(--u)*.8) calc(var(--u)*3) rgba(0,0,0,.7)}
.sbase.act{transition:none;border-color:rgba(255,122,26,.55)}
.snub{position:absolute;border-radius:50%;will-change:transform;transition:transform .14s ease-out;
  background:radial-gradient(circle at 42% 34%,#4a5669 0%,#2a3240 55%,#1b212b 100%);border:1px solid #4a566b;
  box-shadow:0 calc(var(--u)*1) calc(var(--u)*1.8) rgba(0,0,0,.65),inset 0 0 0 calc(var(--u)*1.4) rgba(0,0,0,.18),inset 0 1px 0 rgba(255,255,255,.12)}
.sbase.act .snub{transition:none;background:radial-gradient(circle at 42% 34%,#ffb877 0%,#ff7a1a 55%,#d4500a 100%);border-color:#ffc08f}
.snub.on{box-shadow:0 0 0 calc(var(--u)*1) #fff,0 0 calc(var(--u)*4) #ff7a1a}
.shint{position:absolute;left:0;width:100%;text-align:center;font-size:calc(var(--u)*2.3);font-weight:700;letter-spacing:.06em;color:#566377;white-space:nowrap}
.sbase.act .shint{opacity:0}

/* ---- touchpad ---- */
.tpad{overflow:hidden;border-radius:calc(var(--u)*3.2);border:1px solid #2c3647;
  background:radial-gradient(rgba(150,165,185,.11) 1px,transparent 1.6px) 0 0/calc(var(--u)*3.4) calc(var(--u)*3.4),linear-gradient(180deg,#161d28,#0f141c);
  box-shadow:0 calc(var(--u)*.8) 0 #030508,inset 0 1px 0 rgba(255,255,255,.05)}
.tpad.on{border-color:#ff7a1a;box-shadow:0 0 0 calc(var(--u)*.5) rgba(255,122,26,.25),0 calc(var(--u)*.8) 0 #030508}
.tpad .tlab{position:absolute;left:0;top:50%;width:100%;transform:translateY(-50%);text-align:center;
  font-size:calc(var(--u)*2.6);font-weight:700;letter-spacing:.2em;color:#3e4a5c}
.tdot{position:absolute;left:0;top:0;border-radius:50%;opacity:0;will-change:transform;
  background:radial-gradient(circle,rgba(255,170,110,.95) 0%,rgba(255,122,26,.45) 45%,rgba(255,122,26,0) 70%)}

/* ---- header pill ---- */
#hdr{position:absolute;z-index:5;left:50%;top:8px;transform:translateX(-50%);display:flex;align-items:center;gap:8px;
  height:clamp(26px,calc(var(--u)*8.5),40px);padding:0 10px 0 12px;border-radius:999px;max-width:60%;
  background:rgba(13,18,26,.9);border:1px solid #243145;color:#c9d4e0;
  font-size:clamp(11px,calc(var(--u)*3.3),14px);font-weight:700;white-space:nowrap;box-shadow:0 4px 14px rgba(0,0,0,.35)}
#hname{overflow:hidden;text-overflow:ellipsis;color:#ffa45c;letter-spacing:.02em}
#hdr svg{width:1.3em;height:1.3em;flex:none;opacity:.75}
.dot{display:inline-block;width:8px;height:8px;flex:none;border-radius:50%;background:#e5534b;box-shadow:0 0 0 3px rgba(229,83,75,.18)}
.dot.on{background:#3fb950;box-shadow:0 0 0 3px rgba(63,185,80,.2)}

/* ---- overlays ---- */
.ov{position:absolute;left:0;top:0;width:100%;height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;
  text-align:center;gap:12px;padding:20px}
#msg{z-index:10;background:rgba(6,8,12,.92)}
#menu{z-index:20;background:rgba(3,5,8,.66)}
#rotate{z-index:30;background:#07090d}
#gate{z-index:40;background:radial-gradient(80% 80% at 50% 45%,#141b27,#07090d 70%)}
.big{font-size:20px;font-weight:700;color:#e6edf3;max-width:520px;line-height:1.3}
.sub{font-size:13.5px;color:#8b98a8;max-width:460px;line-height:1.45}
.cta{min-width:140px;height:46px;padding:0 22px;border-radius:999px;background:linear-gradient(180deg,#ff8c33,#e8590c);color:#160b02;font-weight:800;font-size:15px}
.ghost{height:44px;padding:0 20px;border-radius:999px;border:1px solid #2c3647;color:#c9d4e0;font-size:13.5px;font-weight:600;background:#121822}
.spin{width:34px;height:34px;border-radius:50%;border:3px solid #1d2635;border-top-color:#ff7a1a;animation:spin .9s linear infinite}
@keyframes spin{to{transform:rotate(360deg)}}
.logo{font-size:22px;font-weight:800;letter-spacing:.02em}
.logo b{color:#ff7a1a}
.play{width:76px;height:76px;border-radius:50%;display:flex;align-items:center;justify-content:center;color:#160b02;
  background:radial-gradient(circle at 50% 35%,#ffae66,#ff7a1a 60%,#d4500a);
  box-shadow:0 0 0 8px rgba(255,122,26,.14),0 0 40px rgba(255,122,26,.35);animation:pulse 2s ease-in-out infinite}
.play svg{width:34px;height:34px;margin-left:4px}
@keyframes pulse{50%{box-shadow:0 0 0 14px rgba(255,122,26,.07),0 0 50px rgba(255,122,26,.45)}}
.gstat{display:flex;align-items:center;gap:8px;font-size:12.5px;color:#8b98a8}
body:not(.portrait) .ponly{display:none}
.rot{width:116px;height:116px}
.rot .ph{transform-origin:60px 60px;animation:tilt 2.6s ease-in-out infinite}
@keyframes tilt{0%,22%{transform:rotate(0deg)}52%,82%{transform:rotate(-90deg)}100%{transform:rotate(0deg)}}

/* ---- menu ---- */
.sheet{width:min(560px,100%);max-height:100%;overflow:auto;touch-action:pan-y;text-align:left;
  background:#0f141d;border:1px solid #233044;border-radius:18px;padding:14px 16px;box-shadow:0 20px 60px rgba(0,0,0,.6)}
.mrow{display:flex;align-items:center;gap:10px;margin-top:12px}
.mrow:first-child{margin-top:0}
#model{flex:1;min-width:0;font:inherit;color:#e6edf3;background:#1b2533;border:1px solid #3a4b61;border-radius:7px;padding:8px;touch-action:manipulation}
.mname{font-weight:800;font-size:15px;color:#ffa45c;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.mstat{font-size:12px;color:#8b98a8;white-space:nowrap}
.mx{margin-left:auto;flex:none;width:44px;height:40px;border-radius:12px;background:#161d29;border:1px solid #263041;font-size:22px;line-height:1;color:#aeb9c7}
.k{flex:none;width:58px;font-size:11px;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:#6e7b8c}
.seg{display:flex;flex:1;gap:3px;padding:3px;border-radius:12px;background:#0a0e15;border:1px solid #233044}
.seg button{flex:1;height:40px;border-radius:9px;font-size:13px;font-weight:700;color:#aeb9c7}
.seg button.sel{background:linear-gradient(180deg,#ff8c33,#e8590c);color:#160b02}
.tog{flex:1;height:46px;padding:0 12px;border-radius:12px;display:flex;align-items:center;justify-content:space-between;gap:8px;
  background:#151c27;border:1px solid #263041;font-size:13px;font-weight:700;color:#c9d4e0}
.tog i{font-style:normal;font-size:11px;font-weight:800;padding:3px 8px;border-radius:999px;background:#0a0e15;color:#6e7b8c}
.tog.on i{background:#ff7a1a;color:#160b02}
.tog:disabled{opacity:.45}
.hint{margin-top:8px;font-size:12px;color:#f0a35a;line-height:1.4}
.hint:empty{display:none}
.help{margin-top:10px;font-size:11.5px;color:#6e7b8c;line-height:1.45}

@media (prefers-reduced-motion:reduce){.play,.rot .ph,.spin{animation-duration:6s}.sbase,.snub{transition:none}}
</style>
</head>
<body>
<div id="app">
  <div id="surface"></div>

  <button id="hdr" type="button" aria-label="Controller menu" data-i18n-aria="PhoneMenu"><i class="dot" id="hdot"></i><span id="hname" data-i18n="PhoneConnecting">Connecting…</span><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" fill-rule="evenodd" d="M10.4 2.5h3.2l.5 2.6a7.6 7.6 0 0 1 1.9 1.1l2.5-.9 1.6 2.8-2 1.7a7.7 7.7 0 0 1 0 2.2l2 1.7-1.6 2.8-2.5-.9a7.6 7.6 0 0 1-1.9 1.1l-.5 2.6h-3.2l-.5-2.6a7.6 7.6 0 0 1-1.9-1.1l-2.5.9-1.6-2.8 2-1.7a7.7 7.7 0 0 1 0-2.2l-2-1.7 1.6-2.8 2.5.9a7.6 7.6 0 0 1 1.9-1.1zM12 15.2a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4z"/></svg></button>

  <div id="msg" class="ov hide" role="status">
    <div class="spin" id="spin"></div>
    <div class="big" id="msgtitle" data-i18n="PhoneConnectingGameFlow">Connecting to GameFlow…</div>
    <div class="sub" id="msgsub"></div>
    <button id="retry" class="cta hide" type="button" data-i18n="PhoneRetry">Retry</button>
  </div>

  <div id="menu" class="ov hide">
    <div class="sheet" role="dialog" aria-label="Controller settings" data-i18n-aria="PhoneSettings">
      <div class="mrow"><i class="dot" id="mdot"></i><span class="mname" id="mname"></span><span class="mstat" id="mstat"></span><button class="mx" id="mclose" type="button" aria-label="Close" data-i18n-aria="UiClose">×</button></div>
      <div class="mrow"><span class="k" data-i18n="PhoneLayout">Layout</span><div class="seg" id="seg"><button type="button" data-l="xbox">Xbox</button><button type="button" data-l="ds4">PlayStation</button><button type="button" data-l="switch">Nintendo</button></div></div>
      <div class="mrow"><label class="k" for="model" data-i18n="PhoneModel">Model</label><select id="model" aria-label="Controller model" data-i18n-aria="PhoneModel"></select></div>
      <div class="mrow"><button class="tog" id="tmotion" type="button"><span data-i18n="PhoneMotion">Motion</span><i data-i18n="PhoneOff">OFF</i></button><button class="tog" id="thaptics" type="button"><span data-i18n="PhoneHaptics">Haptics</span><i data-i18n="PhoneOn">ON</i></button><button class="tog" id="tfs" type="button"><span data-i18n="PhoneFullscreen">Fullscreen</span><i data-i18n="PhoneOff">OFF</i></button></div>
      <div class="hint" id="mhint"></div>
      <div class="help" data-i18n="PhoneHelp">Sticks float: put your thumb anywhere on that side of the screen. Double-tap a stick side to click the stick (L3/R3). Triggers: tap for a full pull, slide down to ease off. Slide across face buttons or the D-pad to roll between them.</div>
    </div>
  </div>

  <div id="rotate" class="ov hide">
    <svg class="rot" viewBox="0 0 120 120" aria-hidden="true">
      <path d="M86 26A40 40 0 0 0 30 34" fill="none" stroke="#ff7a1a" stroke-width="4" stroke-linecap="round"/>
      <path d="M38.3 30.5 30 34l1.1-8.9" fill="none" stroke="#ff7a1a" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/>
      <g class="ph"><rect x="44" y="28" width="32" height="64" rx="7" fill="#121925" stroke="#d6dee8" stroke-width="4"/><path d="M55 84h10" stroke="#d6dee8" stroke-width="3" stroke-linecap="round"/></g>
    </svg>
    <div class="big" data-i18n="PhoneRotate">Rotate your phone to landscape</div>
    <div class="sub" data-i18n="PhoneRotateHint">Hold it sideways with both hands, like a controller. If the screen doesn't turn, switch off rotation lock.</div>
    <button id="sideways" class="ghost" type="button" data-i18n="PhoneSideways">Use sideways anyway</button>
  </div>

  <div id="gate" class="ov">
    <div class="logo">Game<b>Flow</b></div>
    <div class="play"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5.5v13l11-6.5z" fill="currentColor"/></svg></div>
    <div class="big" data-i18n="PhoneStart">Tap to start</div>
    <div class="sub">Goes fullscreen and keeps the screen awake while you play.</div>
    <div class="gstat"><i class="dot" id="gdot"></i><span id="gstat" data-i18n="PhoneConnecting">Connecting…</span></div>
  </div>
</div>
<script>
"use strict";
var phoneTranslations = /*PHONE_TRANSLATIONS*/{};
function phoneText(key, fallback) { return phoneTranslations[key] || fallback; }
document.querySelectorAll('[data-i18n]').forEach(function(node) {
  var translated = phoneTranslations[node.dataset.i18n];
  if (translated) { node.textContent = translated; }
});
document.querySelectorAll('[data-i18n-aria]').forEach(function(node) {
  var translated = phoneTranslations[node.dataset.i18nAria];
  if (translated) { node.setAttribute('aria-label', translated); }
});

// Bit order is a FIXED wire contract shared with WebControllerProtocol.cs
// on the server. Deliberately not tied to the server's ButtonId enum
// ordering — that can be reordered during refactoring, and this must not
// silently start reporting the wrong buttons if it ever is.
var BIT = {
  south:0, east:1, west:2, north:3,
  lb:4, rb:5, back:6, start:7, guide:8,
  l3:9, r3:10,
  up:11, down:12, left:13, right:14, touchpad:15,
  misc:16
};
var BIT_COUNT = 17;

var LAYOUTS = ["xbox", "ds4", "switch"];
var LAYOUT_KEY = "gameflow-layout";
var MODEL_KEY = "gameflow-model";
var MODELS = {
  xbox: [{ id:"xbox360", label:"Xbox 360" }, { id:"xboxone", label:"Xbox One" }, { id:"xboxseries", label:"Xbox Series X/S" }],
  ds4: [{ id:"ds4", label:"DualShock 4" }, { id:"dualsense", label:"DualSense" }],
  switch: [{ id:"switch", label:"Nintendo Switch Pro" }]
};
var HAPTICS_KEY = "gameflow-haptics";

function noop() {}
function byId(id) { return document.getElementById(id); }
function readPref(key) { try { return window.localStorage.getItem(key); } catch (e) { return null; } }
function writePref(key, value) { try { window.localStorage.setItem(key, value); } catch (e) { /* private mode: not remembered */ } }

var layoutId = readPref(LAYOUT_KEY);
// The touchpad-only layout was replaced by the Nintendo one.
if (layoutId === "touch") { layoutId = "switch"; writePref(LAYOUT_KEY, layoutId); }
if (LAYOUTS.indexOf(layoutId) < 0) { layoutId = "xbox"; }
var modelId = readPref(MODEL_KEY);
if (!MODELS[layoutId].some(function (model) { return model.id === modelId; })) { modelId = MODELS[layoutId][0].id; }
var canVibrate = typeof navigator.vibrate === "function";
var hapticsOn = readPref(HAPTICS_KEY) !== "0";

var state = { buttons:0, lx:0, ly:0, rx:0, ry:0, lt:0, rt:0, touch:{},
              gp:0, gy:0, gr:0, ax:0, ay:0, az:0, gyro:0 };
var ws = null, connected = false, everConnected = false, padIndex = -1, padName = "", dirty = true, rumbleTimer = null;
var terminal = null, backoff = 1000, retryTimer = null, failures = 0;
var started = false, portrait = false, forceSideways = false, sideways = false, menuOpen = false;

var app = byId("app"), surface = byId("surface"), hdr = byId("hdr");

// Stable for this browser tab across WebSocket reconnects. sessionStorage
// keeps two tabs independent (localStorage would make them steal one pad
// from each other) while still letting a dropped Wi-Fi connection reclaim
// the same GameFlow device id and slot assignment.
function newClientId() {
  if (window.crypto && typeof window.crypto.randomUUID === "function") {
    return window.crypto.randomUUID();
  }
  return "web-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2);
}
var clientId;
try {
  clientId = sessionStorage.getItem("gameflow-client-id");
  if (!clientId) {
    clientId = newClientId();
    sessionStorage.setItem("gameflow-client-id", clientId);
  }
} catch (e) {
  clientId = newClientId();
}

// The portable Vibration API is on/off, not variable-amplitude. Model motor
// strength as the duty cycle inside a short period and keep repeating until
// the server sends its explicit zero state. This preserves sustained game
// rumble without leaving the phone buzzing after a disconnect.
function stopRumble() {
  if (rumbleTimer !== null) {
    clearInterval(rumbleTimer);
    rumbleTimer = null;
  }
  if (navigator.vibrate) { navigator.vibrate(0); }
}

function applyRumble(strength, periodMs) {
  stopRumble();
  if (!navigator.vibrate || strength <= 0.02) { return; }

  var period = Math.max(100, Math.min(1000, periodMs || 250));
  var pulse = Math.max(10, Math.round(period * Math.min(1, strength)));
  var vibrate = function () { navigator.vibrate(pulse); };
  vibrate();
  rumbleTimer = setInterval(vibrate, period);
}

// Short click on press. Skipped while game rumble runs: any vibrate()
// call cancels the pulse that is currently playing.
function tick() {
  if (hapticsOn && canVibrate && rumbleTimer === null) {
    try { navigator.vibrate(8); } catch (e) { /* ignored */ }
  }
}

// ---- Geometry. One unit = 1/100 of the play-surface height (or 1/178 of
// its width on squarer screens), so every control keeps its shape and
// the layout only gains free space in the centre on wider phones. ----
var U = 3.6, WU = 200, HU = 100, SX = 0, SY = 0, VW = 0, hdrGap = 44;
var controls = [], bitEls = {}, pointers = {};
// Keep a quick press visible for two game frames, including between sends.
var MIN_TAP_MS = 34;
var MIN_HIT_PX = 24; // radius: every tap target is at least 48 CSS px

var ICON = {
  view: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linejoin="round"><path d="M9 8.5V6a1.5 1.5 0 0 1 1.5-1.5h8A1.5 1.5 0 0 1 20 6v7a1.5 1.5 0 0 1-1.5 1.5H16"/><rect x="4" y="9" width="12" height="10.5" rx="1.5"/></svg>',
  menu: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><path d="M5 7h14M5 12h14M5 17h14"/></svg>',
  home: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 3.5 2.8 11.4h2.7V20h5.2v-5.6h2.6V20h5.2v-8.6h2.7z"/></svg>',
  arrow: '<svg viewBox="0 0 10 10"><path d="M5 2.2 8.6 7.4H1.4z" fill="currentColor"/></svg>',
  cross: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18"/></svg>',
  circle: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.8"><circle cx="12" cy="12" r="7.2"/></svg>',
  square: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.8" stroke-linejoin="round"><rect x="5.5" y="5.5" width="13" height="13" rx="1"/></svg>',
  triangle: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.8" stroke-linejoin="round"><path d="M12 4.5 20.2 18.5H3.8z"/></svg>',
  minus: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M6.5 12h11"/></svg>',
  plus: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M6.5 12h11M12 6.5v11"/></svg>',
  capture: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="5.6" fill="currentColor"/></svg>'
};

// Nintendo letters sit at the SDL positions: bottom B (south), right A
// (east), left Y (west), top X (north). The bits stay positional.
var NIN_FACE = "#dfe2e7";
var FACE_LOOK = {
  xbox: { south:["A", "#5cc93b"], east:["B", "#f0524f"], west:["X", "#3b8eea"], north:["Y", "#f5c518"] },
  ds4:  { south:[ICON.cross, "#8fb2ff"], east:[ICON.circle, "#ff6f6f"], west:[ICON.square, "#f28fd9"], north:[ICON.triangle, "#46dcae"] },
  "switch": { south:["B", NIN_FACE], east:["A", NIN_FACE], west:["Y", NIN_FACE], north:["X", NIN_FACE] }
};

function rgba(hex, a) {
  var n = parseInt(hex.slice(1), 16);
  return "rgba(" + (n >> 16) + "," + ((n >> 8) & 255) + "," + (n & 255) + "," + a + ")";
}
function px(v) { return (Math.round(v * U * 10) / 10) + "px"; }
function place(node, x, y, w, h) {
  var s = node.style;
  s.left = px(x); s.top = px(y); s.width = px(w); s.height = px(h);
}
// Only ever called with the constant markup above, never with server data.
function mk(cls, parent, html) {
  var node = document.createElement("div");
  node.className = cls;
  if (html) { node.innerHTML = html; }
  parent.appendChild(node);
  return node;
}
function dist(pt, x, y) { var dx = pt.x - x, dy = pt.y - y; return Math.sqrt(dx * dx + dy * dy); }
function inRect(pt, x, y, w, h) { return pt.x >= x && pt.x <= x + w && pt.y >= y && pt.y <= y + h; }
function bindBit(bit, node) { (bitEls[bit] || (bitEls[bit] = [])).push(node); }

function measure() {
  VW = window.innerWidth;
  var vh = window.innerHeight;
  portrait = vh > VW;
  // phoneText("PhoneSideways", "Use sideways anyway") (rotation lock on, no orientation API): draw
  // the landscape pad rotated 90 degrees inside the portrait viewport.
  sideways = forceSideways && portrait;
  app.classList.toggle("sideways", sideways);
  app.style.width = sideways ? vh + "px" : "";
  app.style.height = sideways ? VW + "px" : "";
  app.style.left = sideways ? VW + "px" : "";
  var w = Math.max(1, surface.offsetWidth), h = Math.max(1, surface.offsetHeight);
  SX = surface.offsetLeft; SY = surface.offsetTop;
  U = Math.min(h / 100, w / 178);
  WU = w / U; HU = h / U;
  app.style.setProperty("--u", U.toFixed(3) + "px");
  document.body.classList.toggle("portrait", portrait && !sideways);
}

// Client px -> surface units. Uses cached geometry only, so pointer
// handlers never force a layout.
function toLocal(ev) {
  var ax = ev.clientX, ay = ev.clientY;
  if (sideways) { ax = ev.clientY; ay = VW - ev.clientX; }
  return { x: (ax - SX) / U, y: (ay - SY) / U };
}

// ---- Controls. Each is { hit(pt), down(p, pt, ev), move(p, pt, ev), up(p), reset? }.
// A pointer record p carries the button mask it currently holds; the
// pad's button word is the OR of all live pointers, so a released
// pointer can never leave a bit stuck. ----
function makeRound(s) {
  var r = s.d / 2, node = mk("btn" + (s.cls ? " " + s.cls : ""), surface, s.html);
  place(node, s.cx - r, s.cy - r, s.d, s.d);
  if (s.color) {
    node.classList.add("face");
    node.style.setProperty("--fc", s.color);
    node.style.setProperty("--fb", rgba(s.color, 0.45));
  }
  bindBit(s.bit, node);
  var hr = Math.max(r * 1.15, MIN_HIT_PX / U), keep = hr + 6, mask = 1 << s.bit;
  controls.push({
    hit: function (pt) { return dist(pt, s.cx, s.cy) <= hr; },
    down: function (p) { p.mask = mask; },
    move: function (p, pt) { p.mask = dist(pt, s.cx, s.cy) <= keep ? mask : 0; },
    up: noop
  });
}

function makeRect(s) {
  var node = mk("btn " + s.cls, surface, s.html);
  place(node, s.x, s.y, s.w, s.h);
  bindBit(s.bit, node);
  var padY = Math.max(1, MIN_HIT_PX / U - s.h / 2), mask = 1 << s.bit;
  controls.push({
    hit: function (pt) { return inRect(pt, s.x - 1, s.y - padY, s.w + 2, s.h + 2 * padY); },
    down: function (p) { p.mask = mask; },
    move: function (p, pt) { p.mask = inRect(pt, s.x - 6, s.y - padY - 6, s.w + 12, s.h + 2 * padY + 12) ? mask : 0; },
    up: noop
  });
}

// Face diamond: one control owns the whole cluster and re-hit-tests on
// every move, so a thumb can roll from A to B (or rest between two
// buttons and hold both, like on a real pad).
function makeFace(cx, cy, scheme) {
  var OFF = 15.5, D = 16, HR = 11.3, look = FACE_LOOK[scheme];
  var defs = [
    { bit:BIT.north, x:cx, y:cy - OFF, k:"north" },
    { bit:BIT.west, x:cx - OFF, y:cy, k:"west" },
    { bit:BIT.east, x:cx + OFF, y:cy, k:"east" },
    { bit:BIT.south, x:cx, y:cy + OFF, k:"south" }
  ];
  defs.forEach(function (f) {
    var node = mk("btn face", surface, look[f.k][0]);
    place(node, f.x - D / 2, f.y - D / 2, D, D);
    node.style.setProperty("--fc", look[f.k][1]);
    node.style.setProperty("--fb", rgba(look[f.k][1], 0.45));
    bindBit(f.bit, node);
  });
  function maskAt(pt) {
    var m = 0;
    for (var i = 0; i < defs.length; i++) {
      if (dist(pt, defs[i].x, defs[i].y) <= HR) { m |= 1 << defs[i].bit; }
    }
    return m;
  }
  controls.push({
    hit: function (pt) { return dist(pt, cx, cy) <= OFF + HR; },
    down: function (p, pt) { p.mask = maskAt(pt); },
    move: function (p, pt) { p.mask = maskAt(pt); },
    up: noop
  });
}

// 8-way D-pad by angle from its centre: cardinals get 60 degrees,
// diagonals 30, so menus don't catch accidental diagonals.
function dpadMask(dx, dy) {
  var m = Math.sqrt(dx * dx + dy * dy);
  if (m < 3.5 || m > 42) { return 0; }
  var deg = Math.atan2(-dy, dx) * 180 / Math.PI;
  if (deg < 0) { deg += 360; }
  var up = 1 << BIT.up, down = 1 << BIT.down, left = 1 << BIT.left, right = 1 << BIT.right;
  if (deg < 30 || deg >= 330) { return right; }
  if (deg < 60) { return right | up; }
  if (deg < 120) { return up; }
  if (deg < 150) { return up | left; }
  if (deg < 210) { return left; }
  if (deg < 240) { return left | down; }
  if (deg < 300) { return down; }
  return down | right;
}

function makeDpad(cx, cy) {
  var L = 19, A = 6.5, PR = 22.5, rr = px(2.4);
  var plate = mk("dplate", surface);
  place(plate, cx - PR, cy - PR, PR * 2, PR * 2);
  var arms = [
    { bit:BIT.up, x:cx - A, y:cy - L, w:2 * A, h:L - A, rot:0, cut:"borderBottomWidth", rad:[1, 1, 0, 0] },
    { bit:BIT.down, x:cx - A, y:cy + A, w:2 * A, h:L - A, rot:180, cut:"borderTopWidth", rad:[0, 0, 1, 1] },
    { bit:BIT.left, x:cx - L, y:cy - A, w:L - A, h:2 * A, rot:270, cut:"borderRightWidth", rad:[1, 0, 0, 1] },
    { bit:BIT.right, x:cx + A, y:cy - A, w:L - A, h:2 * A, rot:90, cut:"borderLeftWidth", rad:[0, 1, 1, 0] }
  ];
  arms.forEach(function (a) {
    var node = mk("darm", surface, ICON.arrow);
    place(node, a.x, a.y, a.w, a.h);
    node.style[a.cut] = "0";
    node.style.borderRadius = a.rad.map(function (v) { return v ? rr : "0"; }).join(" ");
    node.firstChild.style.transform = "rotate(" + a.rot + "deg)";
    bindBit(a.bit, node);
  });
  var hub = mk("dhub", surface);
  place(hub, cx - A, cy - A, 2 * A, 2 * A);
  controls.push({
    hit: function (pt) { return dist(pt, cx, cy) <= PR + 1; },
    down: function (p, pt) { p.mask = dpadMask(pt.x - cx, pt.y - cy); },
    move: function (p, pt) { p.mask = dpadMask(pt.x - cx, pt.y - cy); },
    up: noop
  });
}

// Trigger: touching it is a full pull (what most games want); sliding the
// thumb down eases off for a partial, analog pull, sliding back up
// restores it. The fill shows the exact value.
function makeTrigger(s) {
  var node = mk("trig", surface), fill = mk("tf", node), lab = mk("tl", node);
  var name = document.createElement("span"); name.textContent = s.label; lab.appendChild(name);
  var pct = document.createElement("span"); pct.className = "tp"; lab.appendChild(pct);
  place(node, s.x, s.y, s.w, s.h);
  var big = px(7), small = px(2.6);
  node.style.borderRadius = s.side === "l"
    ? big + " " + small + " " + small + " " + small
    : small + " " + big + " " + small + " " + small;
  var TRAVEL = 24, shown = -1;
  function show(v) {
    if (state[s.axis] !== v) { state[s.axis] = v; dirty = true; }
    var q = Math.round(v * 100);
    if (q === shown) { return; }
    shown = q;
    fill.style.transform = "scaleY(" + (q / 100) + ")";
    pct.textContent = q > 0 && q < 100 ? q + "%" : "";
    node.classList.toggle("on", q > 0);
  }
  var ctl = {
    isTrigger: true,
    owner: null,
    hit: function (pt) { return inRect(pt, s.x - 1, s.y - 1, s.w + 2, s.h + 2); },
    down: function (p, pt) {
      if (ctl.owner !== null) { return false; }
      ctl.owner = p.id; p.top = pt.y;
      show(1); tick();
    },
    move: function (p, pt) {
      if (pt.y < p.top) { p.top = pt.y; }
      var v = 1 - Math.max(0, pt.y - p.top - 1.5) / TRAVEL;
      show(Math.round(Math.max(0, Math.min(1, v)) * 1000) / 1000);
    },
    up: function (p) { if (ctl.owner === p.id) { ctl.owner = null; show(0); } },
    reset: function () { ctl.owner = null; show(0); }
  };
  controls.push(ctl);
}

// Floating thumbstick: the touch may start anywhere in this side's zone,
// the base jumps to where the thumb lands, a radial deadzone is rescaled
// out, and a double-tap (tap, then touch again and hold) clicks the stick.
var STICK_R = 14, STICK_BASE = 18, STICK_NUB = 7.5, DEADZONE = 0.08;
function makeStick(s) {
  var home = mk("shome", surface);
  place(home, s.hx - STICK_BASE, s.hy - STICK_BASE, STICK_BASE * 2, STICK_BASE * 2);
  var base = mk("sbase", surface);
  place(base, 0, 0, STICK_BASE * 2, STICK_BASE * 2);
  var nub = mk("snub", base), nubOff = STICK_BASE - STICK_NUB;
  place(nub, nubOff, nubOff, STICK_NUB * 2, STICK_NUB * 2);
  var hint = mk("shint", base);
  hint.textContent = s.hint + " · " + phoneText("PhoneDoubleTap", "double-tap");
  hint.style.top = px(STICK_BASE + 8.6);
  bindBit(s.bit, nub);

  var ax = s.side === "l" ? "lx" : "rx", ay = s.side === "l" ? "ly" : "ry";
  var lastTap = null, nubX = 0, nubY = 0, z = s.zone;
  function moveBase(x, y) {
    base.style.transform = "translate3d(" + px(x - STICK_BASE) + "," + px(y - STICK_BASE) + ",0)";
  }
  function moveNub(x, y) {
    x = Math.round(x * 10) / 10; y = Math.round(y * 10) / 10;
    if (x === nubX && y === nubY) { return; }
    nubX = x; nubY = y;
    nub.style.transform = "translate3d(" + px(x) + "," + px(y) + ",0)";
  }
  function setAxes(x, y) {
    x = Math.round(x * 1000) / 1000; y = Math.round(y * 1000) / 1000;
    if (state[ax] !== x || state[ay] !== y) { state[ax] = x; state[ay] = y; dirty = true; }
  }
  function goHome() { base.classList.remove("act"); moveBase(s.hx, s.hy); moveNub(0, 0); setAxes(0, 0); }
  moveBase(s.hx, s.hy);

  var ctl = {
    owner: null,
    hit: function (pt) { return inRect(pt, z.x0, z.y0, z.x1 - z.x0, z.y1 - z.y0); },
    down: function (p, pt) {
      if (ctl.owner !== null) { return false; }
      ctl.owner = p.id;
      var now = performance.now();
      p.x0 = pt.x; p.y0 = pt.y; p.t0 = now; p.travel = 0;
      if (lastTap && now - lastTap.t < 320 && dist(pt, lastTap.x, lastTap.y) < STICK_R * 1.6) {
        p.mask = 1 << s.bit;
      }
      lastTap = null;
      base.classList.add("act");
      moveBase(pt.x, pt.y); moveNub(0, 0); setAxes(0, 0);
    },
    move: function (p, pt) {
      var dx = pt.x - p.x0, dy = pt.y - p.y0, d = Math.sqrt(dx * dx + dy * dy);
      if (d > p.travel) { p.travel = d; }
      var k = d > STICK_R ? STICK_R / d : 1;
      moveNub(dx * k, dy * k);
      var mag = Math.min(1, d / STICK_R);
      if (mag <= DEADZONE) { setAxes(0, 0); return; }
      var scaled = (mag - DEADZONE) / (1 - DEADZONE);
      // Screen Y grows downward, stick Y grows upward.
      setAxes(dx / d * scaled, -dy / d * scaled);
    },
    up: function (p) {
      if (ctl.owner !== p.id) { return; }
      ctl.owner = null;
      var now = performance.now();
      lastTap = (!p.mask && now - p.t0 < 230 && p.travel < STICK_R * 0.35) ? { t: now, x: p.x0, y: p.y0 } : null;
      goHome();
    },
    reset: function () { ctl.owner = null; lastTap = null; goHome(); }
  };
  controls.push(ctl);
}

function makePad(s) {
  var node = mk("tpad", surface);
  place(node, s.x, s.y, s.w, s.h);
  mk("tlab", node).textContent = phoneText("PhoneTouchpad", "TOUCHPAD");
  bindBit(BIT.touchpad, node);
  var DOT = 10, dots = [];
  for (var i = 0; i < 5; i++) {
    var d = mk("tdot", node);
    place(d, 0, 0, DOT, DOT);
    dots.push(d);
  }
  function contact(p, pt, ev) {
    var lx = Math.max(0, Math.min(s.w, pt.x - s.x)), ly = Math.max(0, Math.min(s.h, pt.y - s.y));
    var pressure = ev && typeof ev.pressure === "number" && ev.pressure > 0 ? Math.min(1, ev.pressure) : 1;
    state.touch[p.id] = { i:p.id, x:lx / s.w, y:ly / s.h, p:pressure };
    dirty = true;
    if (p.dot !== undefined) {
      dots[p.dot].style.transform = "translate3d(" + px(lx - DOT / 2) + "," + px(ly - DOT / 2) + ",0)";
    }
  }
  controls.push({
    hit: function (pt) { return inRect(pt, s.x, s.y, s.w, s.h); },
    down: function (p, pt, ev) {
      p.mask = 1 << BIT.touchpad;
      for (var j = 0; j < dots.length; j++) {
        if (!dots[j].busy) { dots[j].busy = true; dots[j].style.opacity = "1"; p.dot = j; break; }
      }
      contact(p, pt, ev);
    },
    move: function (p, pt, ev) { contact(p, pt, ev); },
    up: function (p) {
      delete state.touch[p.id];
      dirty = true;
      if (p.dot !== undefined) { dots[p.dot].busy = false; dots[p.dot].style.opacity = "0"; }
    },
    reset: function () {
      state.touch = {};
      dots.forEach(function (dd) { dd.busy = false; dd.style.opacity = "0"; });
    }
  });
}

// ---- Layouts. Controls are pushed in hit-test priority order: discrete
// buttons first, then clusters, then the generous floating stick zones
// that catch every other touch on their half of the screen. ----
function build() {
  releaseAll();
  surface.textContent = "";
  controls = []; bitEls = {};
  var W = WU, H = HU, mid = W / 2, B = Math.max(0, H - 100);
  var ds4 = layoutId === "ds4", nin = layoutId === "switch";
  surface.className = "lay-" + layoutId;

  // Shoulder row: [LT][LB] ... header ... [RB][RT], widths shrink on 16:9.
  // PlayStation keeps a wider centre for its touchpad.
  var avail = mid - (ds4 ? 30 : 24) - 4, tw = Math.min(40, avail * 0.53), bw = Math.min(34, avail - tw);
  var lbEnd = 4 + tw + bw;
  makeTrigger({ axis:"lt", x:2, y:2, w:tw, h:15, label: ds4 ? "L2" : nin ? "ZL" : "LT", side:"l" });
  makeTrigger({ axis:"rt", x:W - 2 - tw, y:2, w:tw, h:15, label: ds4 ? "R2" : nin ? "ZR" : "RT", side:"r" });
  makeRect({ bit:BIT.lb, x:4 + tw, y:2, w:bw, h:15, html: ds4 ? "L1" : nin ? "L" : "LB", cls:"bump" });
  makeRect({ bit:BIT.rb, x:W - lbEnd, y:2, w:bw, h:15, html: ds4 ? "R1" : nin ? "R" : "RB", cls:"bump" });
  hdrGap = W - 2 * lbEnd - 4;

  // Thumb positions: "outer" sits under a resting thumb, "inner" is the
  // second, lower control, still on the thumb's natural arc.
  var outerY = 47 + B, innerY = 77 + B, padHalf = 0;
  if (ds4) {
    // Large touchpad between the shoulders, Share/Options under its
    // corners, PS at the bottom between the sticks.
    padHalf = Math.min(30, mid - lbEnd - 3);
    makeRect({ bit:BIT.back,  x:mid - padHalf, y:43.5, w:14.5, h:8.5, html:"SHARE", cls:"mini" });
    makeRect({ bit:BIT.start, x:mid + padHalf - 14.5, y:43.5, w:14.5, h:8.5, html:"OPTIONS", cls:"mini" });
    makeRound({ bit:BIT.guide, cx:mid, cy:innerY + 10, d:13, html:"PS", cls:"guide" });
  } else if (nin) {
    // Switch Pro: minus and Capture left of centre, plus and Home right.
    makeRound({ bit:BIT.back,  cx:mid - 16, cy:25, d:12, html:ICON.minus });
    makeRound({ bit:BIT.start, cx:mid + 16, cy:25, d:12, html:ICON.plus });
    makeRound({ bit:BIT.misc,  cx:mid - 9, cy:41, d:12, html:ICON.capture, cls:"cap" });
    makeRound({ bit:BIT.guide, cx:mid + 9, cy:41, d:12.5, html:ICON.home, cls:"guide" });
  } else {
    makeRound({ bit:BIT.guide, cx:mid, cy:24, d:13, html:ICON.home, cls:"guide" });
    makeRound({ bit:BIT.back,  cx:mid - 16, cy:39, d:12.5, html:ICON.view });
    makeRound({ bit:BIT.start, cx:mid + 16, cy:39, d:12.5, html:ICON.menu });
  }

  makeFace(W - 28, outerY, layoutId);
  if (ds4) {
    makeDpad(28, outerY);
    makePad({ x:mid - padHalf, y:12.5, w:padHalf * 2, h:27.5 });
  } else {
    makeDpad(64, innerY);
  }
  makeStick({ side:"l", hx: ds4 ? 64 : 28, hy: ds4 ? innerY : outerY, bit:BIT.l3, hint: ds4 ? "L3" : "LS",
              zone:{ x0:0, x1:mid, y0:18, y1:H } });
  makeStick({ side:"r", hx:W - 64, hy:innerY, bit:BIT.r3, hint: ds4 ? "R3" : "RS",
              zone:{ x0:mid, x1:W, y0:18, y1:H } });
}

function placeHeader() {
  hdr.style.left = (SX + WU * U / 2) + "px";
  hdr.style.top = (SY + Math.max(4, 2 * U)) + "px";
  hdr.style.maxWidth = Math.max(110, hdrGap * U) + "px";
}

function layout() {
  measure();
  build();
  placeHeader();
  render();
}

var layoutQueued = false;
function scheduleLayout() {
  if (layoutQueued) { return; }
  layoutQueued = true;
  requestAnimationFrame(function () { layoutQueued = false; layout(); });
}

// ---- Pointer routing. One listener on the surface hit-tests the layout
// model (no DOM queries per event) and captures the pointer, so moves keep
// arriving even when the finger drifts over the header or off the edge. ----
function syncButtons() {
  var b = 0;
  for (var id in pointers) {
    if (Object.prototype.hasOwnProperty.call(pointers, id)) { b |= pointers[id].mask; }
  }
  var changed = b ^ state.buttons;
  if (!changed) { return; }
  state.buttons = b;
  dirty = true;
  for (var bit = 0; bit < BIT_COUNT; bit++) {
    if (changed & (1 << bit)) {
      var on = (b & (1 << bit)) !== 0, list = bitEls[bit];
      if (list) { for (var k = 0; k < list.length; k++) { list[k].classList.toggle("on", on); } }
    }
  }
  if (changed & b) { tick(); pump(); }
}

function onDown(ev) {
  if (!started || menuOpen || document.hidden) { return; }
  if (ev.pointerType === "mouse" && ev.button !== 0) { return; }
  ev.preventDefault();
  if (pointers[ev.pointerId]) { release(ev.pointerId, true); }
  var pt = toLocal(ev), ctl = null;
  for (var i = 0; i < controls.length; i++) {
    if (controls[i].hit(pt)) { ctl = controls[i]; break; }
  }
  if (!ctl) { return; }
  var p = { id: ev.pointerId, ctl: ctl, mask: 0, pressedAt: performance.now() };
  pointers[ev.pointerId] = p;
  try { surface.setPointerCapture(ev.pointerId); } catch (e) { /* window-level up/cancel still release it */ }
  // A busy single-owner control (stick or trigger already held) keeps the
  // pointer as a dead one rather than letting it fall through.
  if (ctl.down(p, pt, ev) === false) { p.ctl = null; }
  syncButtons();
  pump();
  // Start the minimum hold after the first transmitted edge. Hit testing,
  // painting and browser haptics may have occupied part of this handler.
  p.pressedAt = performance.now();
}

function onMove(ev) {
  var p = pointers[ev.pointerId];
  if (!p || !p.ctl) { return; }
  p.ctl.move(p, toLocal(ev), ev);
  syncButtons();
}

function release(id, force) {
  var p = pointers[id];
  if (!p) { return; }
  clearTimeout(p.releaseTimer);
  var remaining = MIN_TAP_MS - (performance.now() - p.pressedAt);
  if (!force && remaining > 0 && (p.mask || (p.ctl && p.ctl.isTrigger))) {
    p.releaseTimer = setTimeout(function () { release(id); }, Math.ceil(remaining));
    return;
  }
  delete pointers[id];
  if (p.ctl) { p.ctl.up(p); }
  p.mask = 0;
  syncButtons();
  pump();
}

function onUp(ev) { release(ev.pointerId); }

// Stuck-input guard: zero every control, clear every bit. Fingers still
// on the glass are ignored until lifted and placed again.
function releaseAll() {
  Object.keys(pointers).forEach(function (id) { release(id, true); });
  pointers = {};
  controls.forEach(function (c) { if (c.reset) { c.reset(); } });
  state.lx = 0; state.ly = 0; state.rx = 0; state.ry = 0; state.lt = 0; state.rt = 0;
  state.touch = {};
  // Stale rotation rates would read as a constant spin on the PC.
  state.gp = 0; state.gy = 0; state.gr = 0;
  syncButtons();
  dirty = true;
}

function releaseAndFlush() { releaseAll(); pump(); }

surface.addEventListener("pointerdown", onDown);
surface.addEventListener("lostpointercapture", onUp);
window.addEventListener("pointermove", onMove);
window.addEventListener("pointerup", onUp);
window.addEventListener("pointercancel", function(ev) { release(ev.pointerId, true); });
window.addEventListener("contextmenu", function (ev) { ev.preventDefault(); });
document.addEventListener("gesturestart", function (ev) { ev.preventDefault(); });
window.addEventListener("blur", releaseAndFlush);
window.addEventListener("pagehide", function () { releaseAll(); stopRumble(); pump(); });
window.addEventListener("resize", scheduleLayout);
window.addEventListener("orientationchange", function () { setTimeout(scheduleLayout, 150); });
document.addEventListener("visibilitychange", function () {
  if (document.hidden) {
    releaseAll(); stopRumble(); pump();
  } else {
    if (started) { requestWakeLock(); }
    if (!ws && !terminal) { backoff = 1000; connect(); }
  }
});

// ---- Fullscreen, orientation lock, wake lock. All best-effort: iPhone
// Safari has no element fullscreen, and plain-http LAN pages are not a
// secure context, so wake lock may simply not exist. ----
var docEl = document.documentElement;
var fsSupported = !!(docEl.requestFullscreen || docEl.webkitRequestFullscreen) && document.fullscreenEnabled !== false;
var wakeLock = null;

function fsElement() { return document.fullscreenElement || document.webkitFullscreenElement || null; }

function lockLandscape() {
  try {
    if (screen.orientation && typeof screen.orientation.lock === "function") {
      var r = screen.orientation.lock("landscape");
      if (r && typeof r.catch === "function") { r.catch(noop); }
    }
  } catch (e) { /* not supported */ }
}

function enterFullscreen() {
  var r = null;
  try {
    if (docEl.requestFullscreen) { r = docEl.requestFullscreen({ navigationUI: "hide" }); }
    else if (docEl.webkitRequestFullscreen) { r = docEl.webkitRequestFullscreen(); }
  } catch (e) { r = null; }
  if (r && typeof r.then === "function") { r.then(lockLandscape, lockLandscape); }
  else { setTimeout(lockLandscape, 300); }
}

function exitFullscreen() {
  try {
    if (document.exitFullscreen) {
      var r = document.exitFullscreen();
      if (r && typeof r.catch === "function") { r.catch(noop); }
    } else if (document.webkitExitFullscreen) {
      document.webkitExitFullscreen();
    }
  } catch (e) { /* ignored */ }
}

function requestWakeLock() {
  try {
    if (wakeLock || !navigator.wakeLock || typeof navigator.wakeLock.request !== "function" || document.visibilityState !== "visible") { return; }
    navigator.wakeLock.request("screen").then(function (lock) {
      wakeLock = lock;
      lock.addEventListener("release", function () { wakeLock = null; });
    }).catch(noop);
  } catch (e) { /* insecure context or unsupported */ }
}

function onFullscreenChange() { if (menuOpen) { renderMenu(); } scheduleLayout(); }
document.addEventListener("fullscreenchange", onFullscreenChange);
document.addEventListener("webkitfullscreenchange", onFullscreenChange);

function start() {
  if (started) { return; }
  started = true;
  if (fsSupported) { enterFullscreen(); } else { lockLandscape(); }
  requestWakeLock();
  render();
}

// ---- Motion sensors ----
// A phone has a real gyroscope and accelerometer, so it can drive the
// same gyro pipeline a DualSense does. Browsers report rotation in
// DEGREES/second while the desktop side expects SDL's convention of
// RADIANS/second, so convert here rather than special-casing the phone
// downstream. iOS 13+ additionally requires an explicit permission
// prompt triggered by a user gesture — hence the menu toggle.
var DEG2RAD = Math.PI / 180;
var motionOn = false, motionSeen = false, motionHint = "";

function onMotion(ev) {
  if (!motionOn || document.hidden) { return; }
  motionSeen = true;
  var r = ev.rotationRate;
  if (r) {
    // Browser axes -> SDL axes. beta is rotation about the device's X
    // (pitch), alpha about Z as held (yaw when the phone is upright in
    // landscape), gamma about Y (roll).
    state.gp = (r.beta  || 0) * DEG2RAD;
    state.gy = (r.alpha || 0) * DEG2RAD;
    state.gr = (r.gamma || 0) * DEG2RAD;
    state.gyro = 1;
  }
  var g = ev.accelerationIncludingGravity;
  if (g) {
    state.ax = g.x || 0;
    state.ay = g.y || 0;
    state.az = g.z || 0;
    state.gyro = 1;
  }
  dirty = true;
}

function motionFailed(text) { motionOn = false; motionHint = text; renderMenu(); }

function startMotion() {
  motionHint = "";
  var secure = window.isSecureContext !== false;
  if (typeof DeviceMotionEvent === "undefined") {
    motionFailed(secure ? phoneText("PhoneMotionUnavailable", "Motion data unavailable.") : phoneText("PhoneMotionUnavailable", "Motion data unavailable."));
    return;
  }
  var enable = function () {
    window.addEventListener("devicemotion", onMotion);
    motionOn = true; motionSeen = false; renderMenu();
    setTimeout(function () {
      if (motionOn && !motionSeen) {
        motionHint = secure
          ? phoneText("PhoneMotionUnavailable", "Motion data unavailable.")
          : phoneText("PhoneMotionUnavailable", "Motion data unavailable.");
        renderMenu();
      }
    }, 1500);
  };
  // iOS gates sensors behind an explicit permission request that MUST
  // originate from a user gesture; Android and desktop just work.
  if (typeof DeviceMotionEvent.requestPermission === "function") {
    try {
      DeviceMotionEvent.requestPermission().then(function (result) {
        if (result === "granted") { enable(); }
        else { motionFailed(phoneText("PhoneMotionDenied", "Allow motion sensors, then reload.")); }
      }).catch(function () {
        motionFailed(secure ? phoneText("PhoneMotionUnavailable", "Motion data unavailable.") : phoneText("PhoneMotionUnavailable", "Motion data unavailable."));
      });
    } catch (e) {
      motionFailed(phoneText("PhoneMotionUnavailable", "Motion data unavailable."));
    }
  } else {
    enable();
  }
}

function stopMotion() {
  window.removeEventListener("devicemotion", onMotion);
  motionOn = false; motionHint = "";
  state.gyro = 0; state.gp = 0; state.gy = 0; state.gr = 0; state.ax = 0; state.ay = 0; state.az = 0;
  dirty = true;
  renderMenu();
}

// ---- Status, overlays and menu ----
var gate = byId("gate"), rotateOv = byId("rotate"), msg = byId("msg"), menu = byId("menu");
var hName = byId("hname"), hDot = byId("hdot"), gDot = byId("gdot"), gStat = byId("gstat");
var msgTitle = byId("msgtitle"), msgSub = byId("msgsub"), spin = byId("spin"), retryBtn = byId("retry");
var mDot = byId("mdot"), mName = byId("mname"), mStat = byId("mstat"), mHint = byId("mhint");
var tMotion = byId("tmotion"), tHaptics = byId("thaptics"), tFs = byId("tfs");
var segButtons = Array.prototype.slice.call(byId("seg").querySelectorAll("button"));

function live() { return connected && !terminal; }

function padLabel() {
  if (padName) { return padName; }
  if (padIndex >= 0) { return phoneText("PhonePad", "Controller") + " #" + (padIndex + 1); }
  return live() ? phoneText("UiConnected", "Connected") : phoneText("PhoneConnecting", "Connecting…");
}

function statusText() {
  if (terminal === "full") {
    return { title: phoneText("PhoneFull", "All 16 phone slots are in use."), sub: phoneText("PhoneFullHint", "Disconnect another phone in GameFlow, then retry."), retry: phoneText("PhoneRetry", "Retry") };
  }
  if (terminal === "revoked") {
    return { title: phoneText("PhoneRevoked", "This phone was removed in GameFlow. Ask for a new link."), sub: "", retry: "" };
  }
  var stuck = failures >= 3;
  return {
    title: everConnected ? phoneText("PhoneReconnecting", "Connection lost. Reconnecting…") : phoneText("PhoneConnectingGameFlow", "Connecting to GameFlow…"),
    sub: stuck ? phoneText("PhoneUnreachable", "Can't reach the PC. Check that this phone is on the same Wi-Fi network and that GameFlow is running.") : "",
    retry: stuck ? phoneText("PhoneRetry", "Retry now") : ""
  };
}

function render() {
  var on = live(), t = statusText();
  hDot.classList.toggle("on", on);
  hName.textContent = terminal ? phoneText("UiOffline", "Offline") : padLabel();
  gate.classList.toggle("hide", started);
  rotateOv.classList.toggle("hide", !(started && portrait && !sideways));
  msg.classList.toggle("hide", !(started && !on));
  msgTitle.textContent = t.title;
  msgSub.textContent = t.sub;
  msgSub.classList.toggle("hide", !t.sub);
  spin.classList.toggle("hide", !!terminal);
  retryBtn.textContent = t.retry;
  retryBtn.classList.toggle("hide", !t.retry);
  gDot.classList.toggle("on", on);
  gStat.textContent = on ? phoneText("PhoneConnectedAs", "Connected as") + " " + padLabel() : t.title;
  if (menuOpen) { renderMenu(); }
}

function setToggle(btn, on, enabled) {
  btn.classList.toggle("on", on);
  btn.disabled = !enabled;
  btn.querySelector("i").textContent = enabled ? (on ? phoneText("PhoneOn", "ON") : phoneText("PhoneOff", "OFF")) : phoneText("PhoneUnavailable", "Unavailable");
}

function renderMenu() {
  mDot.classList.toggle("on", live());
  mName.textContent = terminal ? phoneText("UiOffline", "Offline") : padLabel();
  mStat.textContent = live() ? phoneText("UiConnected", "Connected") : (terminal ? "" : phoneText("PhoneReconnecting", "Reconnecting…"));
  segButtons.forEach(function (b) { b.classList.toggle("sel", b.getAttribute("data-l") === layoutId); });
  var models = MODELS[layoutId], select = byId("model");
  select.replaceChildren();
  models.forEach(function (model) {
    var option = document.createElement("option");
    option.value = model.id; option.textContent = model.label; select.appendChild(option);
  });
  select.value = modelId;
  setToggle(tMotion, motionOn, true);
  setToggle(tHaptics, hapticsOn && canVibrate, canVibrate);
  tFs.classList.toggle("hide", !fsSupported);
  setToggle(tFs, !!fsElement(), true);
  mHint.textContent = motionHint;
}

function openMenu() {
  if (!started) { return; }
  releaseAndFlush();
  menuOpen = true;
  renderMenu();
  menu.classList.remove("hide");
}

function closeMenu() {
  menuOpen = false;
  menu.classList.add("hide");
}

function setLayout(id) {
  if (LAYOUTS.indexOf(id) < 0 || id === layoutId) { return; }
  layoutId = id;
  modelId = MODELS[id][0].id;
  writePref(LAYOUT_KEY, id);
  writePref(MODEL_KEY, modelId);
  layout();
  dirty = true;
  pump();
}

byId("model").addEventListener("change", function () {
  var value = byId("model").value;
  if (!MODELS[layoutId].some(function (model) { return model.id === value; })) { return; }
  releaseAndFlush();
  modelId = value;
  writePref(MODEL_KEY, value);
  dirty = true;
  pump();
});

hdr.addEventListener("click", openMenu);
byId("mclose").addEventListener("click", closeMenu);
menu.addEventListener("click", function (ev) { if (ev.target === menu) { closeMenu(); } });
segButtons.forEach(function (b) {
  b.addEventListener("click", function () { setLayout(b.getAttribute("data-l")); renderMenu(); });
});
tMotion.addEventListener("click", function () { if (motionOn) { stopMotion(); } else { startMotion(); } });
tHaptics.addEventListener("click", function () {
  hapticsOn = !hapticsOn;
  writePref(HAPTICS_KEY, hapticsOn ? "1" : "0");
  if (hapticsOn) { tick(); }
  renderMenu();
});
tFs.addEventListener("click", function () { if (fsElement()) { exitFullscreen(); } else { enterFullscreen(); } });
gate.addEventListener("click", start);
rotateOv.addEventListener("click", function (ev) {
  if (ev.target === byId("sideways")) { return; }
  // Android: fullscreen + orientation lock turns the screen for the user.
  if (fsSupported) { enterFullscreen(); } else { lockLandscape(); }
});
byId("sideways").addEventListener("click", function () { forceSideways = true; layout(); });
retryBtn.addEventListener("click", retryNow);

// ---- Transport ----
var params = new URLSearchParams(location.search);
var token = params.get("token") || "";
var seatParam = params.get("seat");
var seat = seatParam !== null && /^\d{1,2}$/.test(seatParam) && +seatParam <= 15 ? +seatParam : -1;

function connect() {
  clearTimeout(retryTimer);
  retryTimer = null;
  if (ws || terminal) { return; }
  var proto = location.protocol === "https:" ? "wss:" : "ws:";
  var url = proto + "//" + location.host + "/ws?client=" + encodeURIComponent(clientId) + "&token=" + encodeURIComponent(token)
    + (seat >= 0 ? "&seat=" + seat : "");
  var sock;
  try {
    sock = new WebSocket(url);
  } catch (e) {
    failures++;
    scheduleReconnect();
    render();
    return;
  }
  ws = sock;

  sock.onopen = function () {
    if (ws !== sock) { return; }
    connected = true; everConnected = true; backoff = 1000; failures = 0; dirty = true;
    render();
  };

  sock.onmessage = function (ev) {
    if (ws !== sock) { return; }
    var m;
    try { m = JSON.parse(ev.data); } catch (e) { return; }
    if (!m || typeof m !== "object") { return; }
    if (typeof m.name === "string" && m.name) { padName = m.name.slice(0, 32); }
    if (typeof m.pad === "number") {
      padIndex = m.pad;
      if (m.pad < 0) { terminal = "full"; }
    }
    if (m.full === true) { terminal = "full"; }
    if (m.revoked === true) { terminal = "revoked"; }
    if (m.rumble && navigator.vibrate) {
      var strength = Math.max(m.rumble.low || 0, m.rumble.high || 0);
      applyRumble(strength, m.rumble.ms);
    }
    if (terminal) {
      // Terminal answers stop the reconnect loop; the message stays up
      // until the user explicitly retries.
      connected = false;
      stopRumble();
      releaseAll();
      try { sock.close(); } catch (e) { /* already closing */ }
    }
    render();
  };

  sock.onclose = function () {
    if (ws !== sock) { return; }
    ws = null;
    connected = false;
    stopRumble();
    releaseAll();
    if (!terminal) {
      failures++;
      scheduleReconnect();
    }
    render();
  };

  sock.onerror = function () { try { sock.close(); } catch (e) { /* ignored */ } };
}

// Exponential backoff 1 s, 2 s, 4 s, 8 s, then every 10 s.
function scheduleReconnect() {
  clearTimeout(retryTimer);
  var delay = backoff;
  backoff = Math.min(10000, backoff * 2);
  retryTimer = setTimeout(function () { retryTimer = null; connect(); }, delay);
}

function retryNow() {
  terminal = null; failures = 0; backoff = 1000;
  clearTimeout(retryTimer); retryTimer = null;
  if (ws) {
    var old = ws;
    ws = null;
    try { old.close(); } catch (e) { /* ignored */ }
  }
  render();
  connect();
}

// Send on a fixed cadence rather than per-touch-event: touch devices
// fire move events far faster than any game needs, and this keeps the
// socket from flooding on a busy Wi-Fi network.
function pump() {
  if (document.hidden && (state.buttons || state.lt || state.rt || state.lx || state.ly || state.rx || state.ry)) {
    releaseAll();
  }
  if (connected && dirty && ws && ws.readyState === 1) {
    ws.send(JSON.stringify({
      b: state.buttons,
      lx: +state.lx.toFixed(3), ly: +state.ly.toFixed(3),
      rx: +state.rx.toFixed(3), ry: +state.ry.toFixed(3),
      lt: +state.lt.toFixed(3), rt: +state.rt.toFixed(3),
      touch: Object.keys(state.touch).slice(0, 5).map(function (id) {
        var c = state.touch[id];
        return { i:c.i, x:+c.x.toFixed(4), y:+c.y.toFixed(4), p:+c.p.toFixed(3) };
      }),
      gyro: state.gyro,
      gp: +state.gp.toFixed(4), gy: +state.gy.toFixed(4), gr: +state.gr.toFixed(4),
      ax: +state.ax.toFixed(3), ay: +state.ay.toFixed(3), az: +state.az.toFixed(3),
      layout: modelId
    }));
    dirty = false;
  }
}
setInterval(pump, 16); // ~60 Hz

// Heartbeat so the server's staleness timer doesn't drop an idle-but-present phone.
setInterval(function () { dirty = true; }, 1000);

layout();
connect();
</script>
</body>
</html>

""";
}
