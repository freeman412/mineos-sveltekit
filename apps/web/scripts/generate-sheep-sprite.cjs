/**
 * Minecraft Sheep Sprite Sheet Generator
 * Generates a pixel-art Minecraft sheep for eSheep desktop pet
 *
 * Run with: node scripts/generate-sheep-sprite.js
 */

const fs = require('fs');
const path = require('path');

// Canvas dimensions: 8 columns x 6 rows, each tile 64x64 pixels
const TILE_SIZE = 64;
const TILES_X = 8;
const TILES_Y = 6;
const WIDTH = TILE_SIZE * TILES_X;
const HEIGHT = TILE_SIZE * TILES_Y;

// Minecraft sheep colors
const COLORS = {
  wool: '#E8E8E8',      // Light gray wool
  woolShade: '#C8C8C8', // Shaded wool
  skin: '#FFB5B5',      // Pink skin/face
  skinShade: '#E89898', // Shaded skin
  eye: '#1A1A1A',       // Black eyes
  hoof: '#3D3D3D',      // Dark hooves
  nose: '#FF9999',      // Pink nose
  grass: '#5B8C32',     // Grass for eating animation
};

// Simple pixel art sheep definition (relative to tile center)
// Each frame is defined as colored rectangles
function drawSheep(ctx, x, y, frame, flip = false) {
  const cx = x + TILE_SIZE / 2;
  const cy = y + TILE_SIZE / 2;

  ctx.save();
  if (flip) {
    ctx.translate(x + TILE_SIZE, 0);
    ctx.scale(-1, 1);
    ctx.translate(-x, 0);
  }

  const legOffset = Math.sin(frame * Math.PI / 2) * 3;
  const headBob = Math.sin(frame * Math.PI / 4) * 2;
  const isEating = frame >= 100;
  const isSleeping = frame >= 200;
  const isJumping = frame >= 300;

  // Body (wool) - blocky minecraft style
  ctx.fillStyle = COLORS.wool;
  ctx.fillRect(cx - 18, cy - 8, 36, 20);

  // Wool shading
  ctx.fillStyle = COLORS.woolShade;
  ctx.fillRect(cx - 18, cy + 6, 36, 6);

  // Legs
  ctx.fillStyle = COLORS.woolShade;
  const jumpOffset = isJumping ? -8 : 0;
  // Back legs
  ctx.fillRect(cx - 14, cy + 12 + jumpOffset, 6, 12 - legOffset);
  ctx.fillRect(cx + 8, cy + 12 + jumpOffset, 6, 12 + legOffset);
  // Front legs
  ctx.fillRect(cx - 14 + 20, cy + 12 + jumpOffset, 6, 12 + legOffset);
  ctx.fillRect(cx + 8 - 20, cy + 12 + jumpOffset, 6, 12 - legOffset);

  // Hooves
  ctx.fillStyle = COLORS.hoof;
  ctx.fillRect(cx - 14, cy + 22 + jumpOffset - legOffset, 6, 2);
  ctx.fillRect(cx + 8, cy + 22 + jumpOffset + legOffset, 6, 2);
  ctx.fillRect(cx - 14 + 20, cy + 22 + jumpOffset + legOffset, 6, 2);
  ctx.fillRect(cx + 8 - 20, cy + 22 + jumpOffset - legOffset, 6, 2);

  // Head
  const headY = isSleeping ? cy - 4 : (isEating ? cy + 4 : cy - 12 + headBob);
  const headX = cx + 16;

  // Head wool (top)
  ctx.fillStyle = COLORS.wool;
  ctx.fillRect(headX - 6, headY - 8, 16, 8);

  // Face (skin)
  ctx.fillStyle = COLORS.skin;
  ctx.fillRect(headX - 4, headY, 14, 12);

  // Face shading
  ctx.fillStyle = COLORS.skinShade;
  ctx.fillRect(headX - 4, headY + 8, 14, 4);

  // Eyes
  if (!isSleeping) {
    ctx.fillStyle = COLORS.eye;
    ctx.fillRect(headX + 2, headY + 2, 3, 3);
    ctx.fillRect(headX + 7, headY + 2, 3, 3);
  } else {
    // Sleeping eyes (closed lines)
    ctx.fillStyle = COLORS.eye;
    ctx.fillRect(headX + 1, headY + 3, 5, 1);
    ctx.fillRect(headX + 6, headY + 3, 5, 1);
  }

  // Nose
  ctx.fillStyle = COLORS.nose;
  ctx.fillRect(headX + 4, headY + 7, 4, 2);

  // Grass (eating animation)
  if (isEating) {
    ctx.fillStyle = COLORS.grass;
    ctx.fillRect(headX + 8, headY + 10, 8, 4);
    ctx.fillRect(headX + 10, headY + 6, 4, 4);
  }

  // Ears
  ctx.fillStyle = COLORS.skin;
  ctx.fillRect(headX - 8, headY - 4, 4, 6);
  ctx.fillRect(headX + 14, headY - 4, 4, 6);

  ctx.restore();
}

// Generate frames for different animations
function generateFrames() {
  return [
    // Row 0: Idle (frames 0-3) + Walk Right (frames 4-7)
    { frame: 0, flip: false },   // 0: idle 1
    { frame: 1, flip: false },   // 1: idle 2
    { frame: 0, flip: false },   // 2: idle 3
    { frame: 1, flip: false },   // 3: idle 4
    { frame: 0, flip: false },   // 4: walk right 1
    { frame: 1, flip: false },   // 5: walk right 2
    { frame: 2, flip: false },   // 6: walk right 3
    { frame: 3, flip: false },   // 7: walk right 4

    // Row 1: Walk Left (frames 8-11) + Fall (frames 12-15)
    { frame: 0, flip: true },    // 8: walk left 1
    { frame: 1, flip: true },    // 9: walk left 2
    { frame: 2, flip: true },    // 10: walk left 3
    { frame: 3, flip: true },    // 11: walk left 4
    { frame: 300, flip: false }, // 12: fall 1
    { frame: 301, flip: false }, // 13: fall 2
    { frame: 300, flip: true },  // 14: fall left 1
    { frame: 301, flip: true },  // 15: fall left 2

    // Row 2: Eating (frames 16-19) + Sleep (frames 20-23)
    { frame: 100, flip: false }, // 16: eat 1
    { frame: 101, flip: false }, // 17: eat 2
    { frame: 100, flip: true },  // 18: eat left 1
    { frame: 101, flip: true },  // 19: eat left 2
    { frame: 200, flip: false }, // 20: sleep 1
    { frame: 201, flip: false }, // 21: sleep 2
    { frame: 200, flip: true },  // 22: sleep left 1
    { frame: 201, flip: true },  // 23: sleep left 2

    // Row 3: Jump (frames 24-27) + Drag (frames 28-31)
    { frame: 300, flip: false }, // 24: jump right 1
    { frame: 301, flip: false }, // 25: jump right 2
    { frame: 300, flip: true },  // 26: jump left 1
    { frame: 301, flip: true },  // 27: jump left 2
    { frame: 0, flip: false },   // 28: drag 1
    { frame: 1, flip: false },   // 29: drag 2
    { frame: 0, flip: true },    // 30: drag left 1
    { frame: 1, flip: true },    // 31: drag left 2

    // Row 4: Run Right (frames 32-35) + Run Left (frames 36-39)
    { frame: 0, flip: false },   // 32: run right 1
    { frame: 2, flip: false },   // 33: run right 2
    { frame: 1, flip: false },   // 34: run right 3
    { frame: 3, flip: false },   // 35: run right 4
    { frame: 0, flip: true },    // 36: run left 1
    { frame: 2, flip: true },    // 37: run left 2
    { frame: 1, flip: true },    // 38: run left 3
    { frame: 3, flip: true },    // 39: run left 4

    // Row 5: Extra states (frames 40-47)
    { frame: 0, flip: false },   // 40: stand
    { frame: 1, flip: false },   // 41: stand 2
    { frame: 0, flip: true },    // 42: stand left
    { frame: 1, flip: true },    // 43: stand left 2
    { frame: 100, flip: false }, // 44: eat
    { frame: 101, flip: false }, // 45: eat 2
    { frame: 200, flip: false }, // 46: sleep
    { frame: 201, flip: false }, // 47: sleep 2
  ];
}

// Since we can't use canvas in Node without dependencies,
// let's create an SVG instead that can be viewed/converted
function generateSVG() {
  const frames = generateFrames();
  let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${WIDTH}" height="${HEIGHT}" viewBox="0 0 ${WIDTH} ${HEIGHT}">
  <rect width="100%" height="100%" fill="transparent"/>
  <style>
    .wool { fill: ${COLORS.wool}; }
    .wool-shade { fill: ${COLORS.woolShade}; }
    .skin { fill: ${COLORS.skin}; }
    .skin-shade { fill: ${COLORS.skinShade}; }
    .eye { fill: ${COLORS.eye}; }
    .hoof { fill: ${COLORS.hoof}; }
    .nose { fill: ${COLORS.nose}; }
    .grass { fill: ${COLORS.grass}; }
  </style>
`;

  frames.forEach((frameData, index) => {
    const col = index % TILES_X;
    const row = Math.floor(index / TILES_X);
    const x = col * TILE_SIZE;
    const y = row * TILE_SIZE;

    svg += generateSheepSVG(x, y, frameData.frame, frameData.flip, index);
  });

  svg += '</svg>';
  return svg;
}

function generateSheepSVG(x, y, frame, flip, index) {
  const cx = x + TILE_SIZE / 2;
  const cy = y + TILE_SIZE / 2;

  const legOffset = Math.sin(frame * Math.PI / 2) * 3;
  const headBob = Math.sin(frame * Math.PI / 4) * 2;
  const isEating = frame >= 100 && frame < 200;
  const isSleeping = frame >= 200 && frame < 300;
  const isJumping = frame >= 300;

  let group = `  <g id="frame-${index}" transform="${flip ? `translate(${x + TILE_SIZE}, 0) scale(-1, 1) translate(${-x}, 0)` : ''}">
`;

  const jumpOffset = isJumping ? -8 : 0;

  // Body (wool)
  group += `    <rect class="wool" x="${cx - 18}" y="${cy - 8}" width="36" height="20"/>
`;
  group += `    <rect class="wool-shade" x="${cx - 18}" y="${cy + 6}" width="36" height="6"/>
`;

  // Legs
  group += `    <rect class="wool-shade" x="${cx - 14}" y="${cy + 12 + jumpOffset}" width="6" height="${12 - legOffset}"/>
`;
  group += `    <rect class="wool-shade" x="${cx + 8}" y="${cy + 12 + jumpOffset}" width="6" height="${12 + legOffset}"/>
`;
  group += `    <rect class="wool-shade" x="${cx + 6}" y="${cy + 12 + jumpOffset}" width="6" height="${12 + legOffset}"/>
`;
  group += `    <rect class="wool-shade" x="${cx - 12}" y="${cy + 12 + jumpOffset}" width="6" height="${12 - legOffset}"/>
`;

  // Hooves
  group += `    <rect class="hoof" x="${cx - 14}" y="${cy + 22 + jumpOffset - legOffset}" width="6" height="2"/>
`;
  group += `    <rect class="hoof" x="${cx + 8}" y="${cy + 22 + jumpOffset + legOffset}" width="6" height="2"/>
`;
  group += `    <rect class="hoof" x="${cx + 6}" y="${cy + 22 + jumpOffset + legOffset}" width="6" height="2"/>
`;
  group += `    <rect class="hoof" x="${cx - 12}" y="${cy + 22 + jumpOffset - legOffset}" width="6" height="2"/>
`;

  // Head position
  const headY = isSleeping ? cy - 4 : (isEating ? cy + 4 : cy - 12 + headBob);
  const headX = cx + 16;

  // Head wool
  group += `    <rect class="wool" x="${headX - 6}" y="${headY - 8}" width="16" height="8"/>
`;

  // Face
  group += `    <rect class="skin" x="${headX - 4}" y="${headY}" width="14" height="12"/>
`;
  group += `    <rect class="skin-shade" x="${headX - 4}" y="${headY + 8}" width="14" height="4"/>
`;

  // Eyes
  if (!isSleeping) {
    group += `    <rect class="eye" x="${headX + 2}" y="${headY + 2}" width="3" height="3"/>
`;
    group += `    <rect class="eye" x="${headX + 7}" y="${headY + 2}" width="3" height="3"/>
`;
  } else {
    group += `    <rect class="eye" x="${headX + 1}" y="${headY + 3}" width="5" height="1"/>
`;
    group += `    <rect class="eye" x="${headX + 6}" y="${headY + 3}" width="5" height="1"/>
`;
  }

  // Nose
  group += `    <rect class="nose" x="${headX + 4}" y="${headY + 7}" width="4" height="2"/>
`;

  // Ears
  group += `    <rect class="skin" x="${headX - 8}" y="${headY - 4}" width="4" height="6"/>
`;
  group += `    <rect class="skin" x="${headX + 14}" y="${headY - 4}" width="4" height="6"/>
`;

  // Grass (eating)
  if (isEating) {
    group += `    <rect class="grass" x="${headX + 8}" y="${headY + 10}" width="8" height="4"/>
`;
    group += `    <rect class="grass" x="${headX + 10}" y="${headY + 6}" width="4" height="4"/>
`;
  }

  group += `  </g>
`;
  return group;
}

// Generate and save the SVG
const svg = generateSVG();
const outputPath = path.join(__dirname, '..', 'static', 'minecraft-sheep-sprite.svg');
fs.writeFileSync(outputPath, svg);
console.log(`Generated sprite sheet: ${outputPath}`);
console.log(`Dimensions: ${WIDTH}x${HEIGHT} (${TILES_X}x${TILES_Y} tiles of ${TILE_SIZE}x${TILE_SIZE})`);
