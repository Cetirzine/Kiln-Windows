// Render the shared vector logo; requires sharp (build time only).
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
async function main() {
  const assets = path.resolve(__dirname, '../Kiln.App/Assets');
  const svg = fs.readFileSync(path.resolve(__dirname, '../assets/logo.svg'));
  // Windows taskbar icons need the tile to fill the canvas, without macOS's 100px inset.
  const tile = await sharp(svg).extract({left:100,top:100,width:824,height:824}).png().toBuffer();
  const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
  const frames = await Promise.all(sizes.map(size => sharp(tile).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + sizes.length * 16); header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  frames.forEach((frame, index) => {
    const entry = 6 + index * 16; const size = sizes[index];
    header[entry] = header[entry + 1] = size === 256 ? 0 : size;
    header.writeUInt16LE(1, entry + 4); header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(frame.length, entry + 8); header.writeUInt32LE(offset, entry + 12); offset += frame.length;
  });
  fs.writeFileSync(path.join(assets, 'AppIcon.ico'), Buffer.concat([header, ...frames]));
  for (const [name, width, height] of [
    ['AppIcon.png',256,256], ['Square150x150Logo.scale-200.png',300,300],
    ['Square44x44Logo.scale-200.png',88,88], ['Square44x44Logo.targetsize-24_altform-unplated.png',24,24],
    ['Square44x44Logo.targetsize-48_altform-lightunplated.png',48,48], ['StoreLogo.png',50,50],
    ['SplashScreen.scale-200.png',1240,600], ['Wide310x150Logo.scale-200.png',620,300], ['LockScreenLogo.scale-200.png',48,48],
  ]) await sharp(tile).resize(width,height,{fit:'contain',background:'#00000000'}).png().toFile(path.join(assets,name));
  console.log('Rendered shared Kiln logo: 10 ICO sizes and 9 PNG assets.');
}
main().catch(error=>{ console.error(error); process.exit(1); });
