// Import the supplied vector packs as native UI resources.
// Pass the Controllers directory and the installed sharp module path.
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { mkdir, readFile, readdir, copyFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const sharp = require(process.argv[3] || 'sharp');
const repository = fileURLToPath(new URL('../', import.meta.url));
const source = path.resolve(process.argv[2] || path.join(repository, '../Controllers'));
const destination = path.join(repository, 'src/GameFlow.App/Assets/InputAssets');
const inventory = [];

async function importDirectory(directory, output, pack) {
  await mkdir(output, { recursive: true });
  const files = await readdir(directory, { withFileTypes: true });
  files.sort((left, right) => left.name.localeCompare(right.name, 'en'));
  for (const file of files) {
    const input = path.join(directory, file.name);
    if (file.isDirectory()) {
      await importDirectory(input, path.join(output, file.name), pack);
      continue;
    }
    if (!/\.svg$/i.test(file.name)) continue;
    const bytes = await readFile(input);
    const raster = path.join(output, file.name.replace(/\.svg$/i, '.png'));
    await copyFile(input, path.join(output, file.name));
    await sharp(bytes).resize(96, 96, { fit: 'inside' }).png().toFile(raster);
    inventory.push({
      pack,
      source: path.relative(source, input).replaceAll('\\', '/'),
      sha256: createHash('sha256').update(bytes).digest('hex'),
      resource: path.relative(destination, raster).replaceAll('\\', '/')
    });
  }
}

await importDirectory(path.join(source, 'Controller Prompt Asset Pack'), path.join(destination, 'Prompts'), 'prompts');
await importDirectory(path.join(source, 'Connection Icon Pack'), path.join(destination, 'Icons'), 'icons');
const license = await readFile(path.join(source, 'LICENSE'), 'utf8');
await writeFile(path.join(destination, 'LICENSE.txt'), license.replace(/[ \t]+$/gm, ''));
await writeFile(path.join(destination, 'manifest.json'), JSON.stringify(inventory, null, 2) + '\n');
console.log(`Imported ${inventory.length} vector assets with source hashes and PNG resources.`);
