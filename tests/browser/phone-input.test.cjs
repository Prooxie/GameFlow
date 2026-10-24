const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const browserType = require(process.env.GAMEFLOW_PLAYWRIGHT_MODULE || 'playwright')[process.env.GAMEFLOW_TEST_BROWSER || 'chromium'];
const source = fs.readFileSync(path.join(__dirname, '../../src/GameFlow.Infrastructure/Runtime/Web/WebControllerAssets.cs'), 'utf8');
const html = source.split('internal const string ControllerPage = """')[1].split('""";')[0];
let browser, server, address;
before(async () => {
  server = http.createServer((_, response) => { response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(html); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  address = `http://127.0.0.1:${server.address().port}/?token=test&seat=0`;
  browser = await browserType.launch({ headless: true });
});
after(async () => { await browser?.close(); await new Promise(resolve => server?.close(resolve)); });

async function controller(hapticDelay = 0) {
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 }, hasTouch: true });
  // Keep these input tests at a fixed viewport; fullscreen has its own
  // asynchronous resize and can replace controls while a coordinate is read.
  await page.addInitScript(() => Object.defineProperty(document, 'fullscreenEnabled', { get: () => false }));
  if (hapticDelay) {
    await page.addInitScript(delay => {
      Object.defineProperty(navigator, 'vibrate', { value: () => {
        const until = performance.now() + delay;
        while (performance.now() < until) { /* simulate a busy browser haptics call */ }
        return true;
      } });
    }, hapticDelay);
  }
  await page.routeWebSocket('**/ws?*', route => { route.send(JSON.stringify({ pad: 0, name: 'Test phone' })); });
  await page.goto(address);
  await page.getByText('Tap to start', { exact: true }).tap();
  await page.waitForFunction(() => connected);
  await page.locator('.face').last().waitFor({ state: 'visible' });
  await page.evaluate(() => {
    window.sent = [];
    const send = ws.send.bind(ws);
    ws.send = data => { sent.push({ time: performance.now(), ...JSON.parse(data) }); send(data); };
  });
  return page;
}

test('a quick touch sends a button edge immediately and keeps it through two game frames', async () => {
  const page = await controller(20);
  try {
    const box = await page.locator('.face').last().boundingBox();
    await page.touchscreen.tap(box.x + box.width / 2, box.y + box.height / 2);
    await page.waitForFunction(() => sent.some(frame => frame.b) && sent.at(-1).b === 0, null, { timeout: 5000 });
    const frames = await page.evaluate(() => sent);
    const down = frames.findIndex(frame => frame.b !== 0);
    const up = frames.findIndex((frame, index) => index > down && frame.b === 0);
    assert.ok(down >= 0 && up > down);
    assert.ok(frames[up].time - frames[down].time >= 30, 'short taps must survive polling');
  } finally { await page.close(); }
});

test('trigger taps are also observable', async () => {
  const page = await controller();
  try {
    const box = await page.locator('.trig').last().boundingBox();
    await page.touchscreen.tap(box.x + box.width / 2, box.y + box.height / 2);
    await page.waitForFunction(() => sent.some(frame => frame.rt > 0) && sent.at(-1).rt === 0, null, { timeout: 5000 });
    const frames = await page.evaluate(() => sent);
    assert.ok(frames.some(frame => frame.rt === 1));
  } finally { await page.close(); }
});

for (const event of ['blur', 'pointercancel']) {
  test(`${event} releases a new press immediately`, async () => {
    const page = await controller();
    try {
      const box = await page.locator('.face').last().boundingBox();
      const result = await page.evaluate(({ box, event }) => {
        const detail = { pointerId: 99, pointerType: 'touch', clientX: box.x + box.width / 2, clientY: box.y + box.height / 2 };
        surface.dispatchEvent(new PointerEvent('pointerdown', detail));
        const pressed = state.buttons;
        window.dispatchEvent(event === 'blur' ? new Event('blur') : new PointerEvent('pointercancel', detail));
        return { pressed, released: state.buttons, final: sent.at(-1) };
      }, { box, event });
      assert.notEqual(result.pressed, 0);
      assert.equal(result.released, 0);
      assert.equal(result.final.b, 0);
    } finally { await page.close(); }
  });
}

test('phone model choices stay within the selected family and are sent to GameFlow', async () => {
  const page = await controller();
  try {
    await page.locator('#hdr').click();
    assert.deepEqual(await page.locator('#model option').evaluateAll(options => options.map(option => option.value)), ['xbox360', 'xboxone', 'xboxseries']);
    await page.locator('#model').selectOption('xboxseries');
    await page.waitForFunction(() => sent.at(-1).layout === 'xboxseries');
    await page.locator('[data-l="ds4"]').click();
    assert.deepEqual(await page.locator('#model option').evaluateAll(options => options.map(option => option.value)), ['ds4', 'dualsense']);
    await page.locator('#model').selectOption('dualsense');
    await page.waitForFunction(() => sent.at(-1).layout === 'dualsense');
    await page.reload();
    await page.getByText('Tap to start', { exact: true }).tap();
    assert.equal(await page.evaluate(() => modelId), 'dualsense');
    await page.locator('#hdr').click();
    await page.locator('[data-l="switch"]').click();
    assert.deepEqual(await page.locator('#model option').evaluateAll(options => options.map(option => option.value)), ['switch']);
    assert.equal(await page.evaluate(() => modelId), 'switch');
  } finally { await page.close(); }
});
