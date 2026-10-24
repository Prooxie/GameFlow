const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const browserType = require('playwright')[process.env.GAMEFLOW_TEST_BROWSER || 'chromium'];
const root = path.resolve(__dirname, '../../docs');
let browser, server, address;

before(async () => {
  server = http.createServer((request, response) => {
    const relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const file = path.resolve(root, '.' + (relative === '/' ? '/index.html' : relative));
    if (!file.startsWith(root + path.sep) || !fs.existsSync(file)) {
      response.writeHead(404).end();
      return;
    }
    response.setHeader('Content-Type', file.endsWith('.html') ? 'text/html; charset=utf-8'
      : file.endsWith('.svg') ? 'image/svg+xml' : 'image/png');
    fs.createReadStream(file).pipe(response);
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  address = `http://127.0.0.1:${server.address().port}/`;
  browser = await browserType.launch({ headless: true });
});

after(async () => {
  await browser?.close();
  await new Promise(resolve => server?.close(resolve));
});

test('the project page loads all showcase images and local links', async () => {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const failures = [];
  page.on('response', response => { if (response.status() >= 400) failures.push(response.url()); });
  await page.goto(address);
  await page.locator('#showcase').scrollIntoViewIfNeeded();
  const images = page.locator('#showcase img');
  assert.equal(await images.count(), 3);
  for (const image of await images.all()) {
    await image.scrollIntoViewIfNeeded();
    await image.evaluate(element => element.decode());
    assert.ok(await image.evaluate(element => element.naturalWidth > 1000));
  }
  for (const fragment of await page.locator('a[href^="#"]').evaluateAll(links => links.map(link => link.hash.slice(1)))) {
    assert.ok(await page.locator(`[id="${fragment}"]`).count(), fragment);
  }
  assert.deepEqual(failures, []);
  await page.close();
});

test('the project page fits a narrow phone viewport', async () => {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  await page.goto(address);

  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth));
  assert.equal(await page.locator('h1').count(), 1);
  await page.close();
});
