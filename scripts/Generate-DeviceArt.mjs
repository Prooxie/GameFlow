// Rebuild the original schematic artwork. Install sharp locally or pass its module directory.
// node scripts/Generate-DeviceArt.mjs /absolute/path/to/node_modules/sharp
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const require = createRequire(import.meta.url);
const sharp = require(process.argv[2] || 'sharp');
const root = fileURLToPath(new URL('../themes/', import.meta.url));
const svg = (body, w=800, h=520) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><g stroke="#75859a" stroke-width="3" fill="#202a38">${body}</g></svg>`;
const circle = (x,y,r,fill='#111b29') => `<circle cx="${x}" cy="${y}" r="${r}" fill="${fill}"/>`;
const rect = (x,y,w,h,r=14) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}"/>`;
const label = (x,y,text,size=18) => `<text x="${x}" y="${y}" text-anchor="middle" fill="#dde8f7" stroke="none" font-family="sans-serif" font-size="${size}">${text}</text>`;
for (const [folder,name,kind] of [
  ['steam-controller-default','Steam Controller v1 (2015, schematic)','steam1'],
  ['steam-controller-v2-default','Steam Controller v2 (2026, schematic)','steam2'],
  ['generic-joystick-default','Generic joystick (schematic)','joystick'],
  ['generic-wheel-default','Generic wheel (schematic)','wheel'],
  ['arcade-stick-default','Arcade stick (schematic)','arcade']
]) {
  const directory=path.join(root,folder); await mkdir(directory,{recursive:true});
  const children=[]; let art='';
  const image=(name,x,y,width,height,extra={})=>({type:'image',image:name,x,y,width,height,...extra});
  async function asset(name,body,w,h) { const source=svg(body,w,h); await writeFile(path.join(directory,name+'.svg'),source); await sharp(Buffer.from(source)).png().toFile(path.join(directory,name+'.png')); }
  await asset('active',circle(24,24,20,'#6be4d5'),48,48);
  await asset('stick',circle(36,36,32,'#405168')+circle(36,36,22,'#172333'),72,72);
  function button(x,y,text,input) {art+=circle(x,y,23)+label(x,y+6,text);children.push({type:'showhide',input,children:[image('active.png',x-24,y-24,48,48)]});}
  function stick(x,y,input) {art+=circle(x,y,49); children.push({type:'slider',x,y,inputX:input+':x * 16',inputY:input+':y * 16',children:[image('stick.png',-36,-36,72,72),{type:'showhide',input:input+':click',children:[image('active.png',-24,-24,48,48)]}]});}
  if (kind.startsWith('steam')) {
    art+='<path d="M190 110 Q100 125 86 250 L48 432 Q55 492 114 476 L240 388 L560 388 L686 476 Q748 492 752 432 L714 250 Q700 125 610 110 Z"/>';
    art+=rect(170,76,130,28)+rect(500,76,130,28);
    button(370,165,'‹','back');button(430,165,'≡','start');button(400,240,'S','home');
    if(kind==='steam1') {
      art+=circle(212,226,74)+circle(588,226,74)+label(212,232,'+',38)+label(588,232,'·',38);
      stick(278,332,'stick_left');
      button(522,334,'A','quad_right:s');button(564,292,'B','quad_right:e');button(480,292,'X','quad_right:w');button(522,250,'Y','quad_right:n');
    } else {
      stick(252,188,'stick_left');stick(548,188,'stick_right');
      art+=rect(218,280,116,104,22)+rect(466,280,116,104,22)+label(276,342,'·',32)+label(524,342,'·',32);
      button(164,310,'+','quad_left:n');
      button(648,218,'A','quad_right:s');button(687,176,'B','quad_right:e');button(609,176,'X','quad_right:w');button(648,134,'Y','quad_right:n');
    }
  } else if(kind==='joystick') {
    art+=rect(176,296,448,160,46)+rect(542,130,48,210,20)+label(566,120,'T');
    art+='<path d="M322 345 L340 177 Q400 118 460 177 L478 345 Z"/>';
    stick(400,190,'stick_left');button(482,160,'1','quad_right:s');button(500,220,'2','quad_right:e');button(235,350,'3','quad_right:w');button(565,350,'4','quad_right:n');
    children.push({type:'pbar',x:552,y:144,width:28,height:178,input:'triggers:r:analog',direction:'up',foreground:'FF6BE4D5',background:'22111B29'});
  } else if(kind==='wheel') {
    art+=rect(222,326,356,130,30); await asset('wheel','<circle cx="180" cy="180" r="150" fill="none" stroke="#405168" stroke-width="42"/><path d="M180 180 L50 140 M180 180 L310 140 M180 180 L180 325" stroke="#75859a" stroke-width="28"/>'+circle(180,180,52),360,360);
    children.push({type:'slider',x:400,y:240,inputR:'stick_left:x * 130',children:[image('wheel.png',-180,-180,360,360)]});
    button(270,420,'A','quad_right:s');button(330,420,'B','quad_right:e');button(470,420,'X','quad_right:w');button(530,420,'Y','quad_right:n');
  } else {
    art+=rect(70,180,660,264,40);stick(235,300,'stick_left');
    for(const [i,input] of ['quad_right:w','quad_right:n','bumpers:r','quad_right:s','quad_right:e','triggers:r:stage2'].entries())button(440+i%3*88,268+Math.floor(i/3)*88,String(i+1),input);
  }
  await asset('base',art,800,520);
  const document={name,version:1,width:800,height:520,_comment:'Original GameFlow schematic. Hardware appearance is illustrative; input and output support depend on the backend.',children:[image('base.png',0,0,800,520),...children]};
  await writeFile(path.join(directory,'theme.json'),JSON.stringify(document,null,2)+'\n');
}
