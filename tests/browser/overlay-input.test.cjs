const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const browserType = require(process.env.GAMEFLOW_PLAYWRIGHT_MODULE || 'playwright')[process.env.GAMEFLOW_TEST_BROWSER || 'chromium'];
const source = fs.readFileSync(path.join(__dirname, '../../src/GameFlow.Infrastructure/Runtime/Web/Overlay/OverlayAssets.cs'), 'utf8');
const html = source.split('internal const string OverlayPage = """')[1].split('""";')[0];
let browser, server, address;
before(async () => {
  server = http.createServer((_, response) => { response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(html); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  address = `http://127.0.0.1:${server.address().port}/overlay?text=1&holds=1`;
  browser = await browserType.launch({ headless: true });
});
after(async () => { await browser?.close(); await new Promise(resolve => server?.close(resolve)); });
const element = (kind, x, y, width, height, label, key = 0, flag = 0) =>
  ({ kind, x, y, width, height, radius: 4, label, key, flag });
// Representative nodes of the shared scene; C# tests check the complete 104-key geometry.
const program = {
  type: 'program', themeId: 'native', width: 300, height: 200, images: [], nodes: [],
  keyLabels: { 124: 'F13' }, axisLabels: ['Left stick', 'Right stick', 'Left trigger', 'Right trigger', 'Mouse movement'],
  inputSurface: { elements: [element('panel', 0, 0, 300, 200, ''),
    element('key', 10, 10, 40, 40, 'W', 87),
    element('button', 70, 10, 40, 40, 'Up', 0, 32),
    element('button', 120, 10, 40, 40, 'Down', 0, 64),
    element('motion', 170, 70, 60, 60, '')] }
};
async function overlay() {
  const page = await browser.newPage({ viewport: { width: 600, height: 445 } });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  let socket;
  await page.routeWebSocket('**/overlay/ws?*', route => { socket = route; route.send(JSON.stringify(program)); });
  await page.goto(address);
  await page.waitForFunction(() => document.getElementById('msg').className === 'hide');
  return { page, errors, sendProgram: value => socket.send(JSON.stringify(value)), send: frame => socket.send(JSON.stringify({ type: 'frame', v: [], buttons: [], ...frame })) };
}
async function pixel(page, x, y) {
  return page.evaluate(({ x, y }) => [...document.querySelector('canvas').getContext('2d').getImageData(x, y, 1, 1).data], { x, y });
}
test('native keys and wheel directions render their own raw state', async () => {
  const { page, send, errors } = await overlay();
  try {
    send({ keys: [87], mouse: 32, motion: [.8, -.5] });
    await page.waitForFunction(() => document.querySelector('#inputs').textContent.includes('W'));
    await page.waitForTimeout(50);
    assert.deepEqual(await pixel(page, 36, 38), [101, 211, 132, 255]);
    assert.deepEqual(await pixel(page, 156, 38), [101, 211, 132, 255]);
    assert.deepEqual(await pixel(page, 256, 38), [43, 54, 68, 255]);
    // Movement shares the native 60px compass and 25px reach, with Y up.
    assert.deepEqual(await pixel(page, 430, 218), [140, 200, 255, 255]);
    send({ keys: [], mouse: 64, motion: [0, 0] });
    await page.waitForFunction(() => !document.querySelector('#inputs').textContent.includes('W'));
    await page.waitForTimeout(50);
    assert.deepEqual(await pixel(page, 36, 38), [43, 54, 68, 255]);
    assert.deepEqual(await pixel(page, 156, 38), [43, 54, 68, 255]);
    assert.deepEqual(await pixel(page, 256, 38), [101, 211, 132, 255]);
    assert.deepEqual(errors, []);
  } finally { await page.close(); }
});
test('hold time grows only while an input is down and is removed on release', async () => {
  const { page, send } = await overlay();
  try {
    send({ keys: [87], mouse: 0 });
    await page.waitForFunction(() => /W 0\.[12] s/.test(document.querySelector('#inputs').textContent));
    send({ keys: [], mouse: 0 });
    await page.waitForFunction(() => document.querySelector('#inputs').childElementCount === 0);
  } finally { await page.close(); }
});
test('controller labels are rendered as text and work without a native surface', async () => {
  const { page, send, sendProgram } = await overlay();
  try {
    // Include a label supplied by the server, never interpret it as HTML.
    const label = '<img src=x onerror="window.injected=true">';
    sendProgram({ ...program, inputSurface: null, controlLabels: { 1: label } });
    send({ buttons: [1], keys: [] });
    await page.waitForFunction(() => document.querySelector('#inputs').textContent.includes('<img'));
    assert.equal(await page.locator('#inputs img').count(), 0);
  } finally { await page.close(); }
});

test('extra keyboard keys and mouse motion have names without repeating mapped gamepad buttons', async () => {
  const { page, send } = await overlay();
  try {
    send({ keys: [124], buttons: [1], mouse: 0, activeAxes: [0, 4] });
    await page.waitForFunction(() => document.querySelector('#inputs').textContent.includes('F13'));
    const text = await page.locator('#inputs').textContent();
    assert.match(text, /Mouse movement/);
    assert.doesNotMatch(text, /Left stick/);
    assert.equal(await page.locator('#inputs span').count(), 2);
  } finally { await page.close(); }
});

test('controller analog inputs are named and clear when centred', async () => {
  const { page, send, sendProgram } = await overlay();
  try {
    sendProgram({ ...program, inputSurface: null });
    send({ activeAxes: [0, 3] });
    await page.waitForFunction(() => document.querySelector('#inputs').textContent.includes('Right trigger'));
    assert.match(await page.locator('#inputs').textContent(), /Left stick/);
    send({ activeAxes: [] });
    await page.waitForFunction(() => document.querySelector('#inputs').childElementCount === 0);
  } finally { await page.close(); }
});
