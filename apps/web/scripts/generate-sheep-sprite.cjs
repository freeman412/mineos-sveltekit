/**
 * Minecraft Sheep Sprite Sheet Generator - 3D Style
 * Generates a 3D-looking pixel-art Minecraft sheep like eSheep
 *
 * Run with: node scripts/generate-sheep-sprite.cjs
 */

const fs = require('fs');
const path = require('path');

// Canvas dimensions: 8 columns x 6 rows, each tile 64x64 pixels
const TILE_SIZE = 64;
const TILES_X = 8;
const TILES_Y = 6;
const WIDTH = TILE_SIZE * TILES_X;
const HEIGHT = TILE_SIZE * TILES_Y;

// Enhanced 3D Minecraft sheep colors with more shading levels
const COLORS = {
  // Wool - fluffy white with 3D shading
  woolHighlight: '#FFFFFF',   // Top highlight
  wool: '#F0F0F0',            // Main wool color
  woolMid: '#D8D8D8',         // Mid tone
  woolShade: '#C0C0C0',       // Shaded areas
  woolDark: '#A0A0A0',        // Deep shadows
  woolOutline: '#888888',     // Outline/edge

  // Skin/Face - pink with depth
  skinHighlight: '#FFD4D4',   // Highlight
  skin: '#FFBABA',            // Main skin
  skinShade: '#E8A0A0',       // Shaded skin
  skinDark: '#D08888',        // Deep shadow

  // Features
  eye: '#202020',             // Dark eyes
  eyeHighlight: '#404040',    // Eye highlight
  hoof: '#484848',            // Hooves
  hoofHighlight: '#606060',   // Hoof highlight
  nose: '#FF9090',            // Pink nose
  noseHighlight: '#FFA8A8',   // Nose highlight

  // Environment
  grass: '#5B8C32',           // Grass
  grassLight: '#7AAC52',      // Grass highlight
};

// Generate frames for different animations
function generateFrames() {
  return [
    // Row 0: Idle (frames 0-3) + Walk Right (frames 4-7)
    { frame: 0, flip: false },
    { frame: 1, flip: false },
    { frame: 0, flip: false },
    { frame: 1, flip: false },
    { frame: 0, flip: false },
    { frame: 1, flip: false },
    { frame: 2, flip: false },
    { frame: 3, flip: false },

    // Row 1: Walk Left (frames 8-11) + Fall (frames 12-15)
    { frame: 0, flip: true },
    { frame: 1, flip: true },
    { frame: 2, flip: true },
    { frame: 3, flip: true },
    { frame: 300, flip: false },
    { frame: 301, flip: false },
    { frame: 300, flip: true },
    { frame: 301, flip: true },

    // Row 2: Eating (frames 16-19) + Sleep (frames 20-23)
    { frame: 100, flip: false },
    { frame: 101, flip: false },
    { frame: 100, flip: true },
    { frame: 101, flip: true },
    { frame: 200, flip: false },
    { frame: 201, flip: false },
    { frame: 200, flip: true },
    { frame: 201, flip: true },

    // Row 3: Jump (frames 24-27) + Drag (frames 28-31)
    { frame: 300, flip: false },
    { frame: 301, flip: false },
    { frame: 300, flip: true },
    { frame: 301, flip: true },
    { frame: 0, flip: false },
    { frame: 1, flip: false },
    { frame: 0, flip: true },
    { frame: 1, flip: true },

    // Row 4: Run Right (frames 32-35) + Run Left (frames 36-39)
    { frame: 0, flip: false },
    { frame: 2, flip: false },
    { frame: 1, flip: false },
    { frame: 3, flip: false },
    { frame: 0, flip: true },
    { frame: 2, flip: true },
    { frame: 1, flip: true },
    { frame: 3, flip: true },

    // Row 5: Extra states (frames 40-47)
    { frame: 0, flip: false },
    { frame: 1, flip: false },
    { frame: 0, flip: true },
    { frame: 1, flip: true },
    { frame: 100, flip: false },
    { frame: 101, flip: false },
    { frame: 200, flip: false },
    { frame: 201, flip: false },
  ];
}

function generateSVG() {
  const frames = generateFrames();
  let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${WIDTH}" height="${HEIGHT}" viewBox="0 0 ${WIDTH} ${HEIGHT}">
  <defs>
    <!-- Wool texture gradient for 3D effect -->
    <linearGradient id="woolGrad" x1="0%" y1="0%" x2="0%" y2="100%">
      <stop offset="0%" style="stop-color:${COLORS.woolHighlight}"/>
      <stop offset="30%" style="stop-color:${COLORS.wool}"/>
      <stop offset="70%" style="stop-color:${COLORS.woolMid}"/>
      <stop offset="100%" style="stop-color:${COLORS.woolShade}"/>
    </linearGradient>
    <linearGradient id="skinGrad" x1="0%" y1="0%" x2="0%" y2="100%">
      <stop offset="0%" style="stop-color:${COLORS.skinHighlight}"/>
      <stop offset="50%" style="stop-color:${COLORS.skin}"/>
      <stop offset="100%" style="stop-color:${COLORS.skinShade}"/>
    </linearGradient>
    <linearGradient id="legGrad" x1="0%" y1="0%" x2="100%" y2="0%">
      <stop offset="0%" style="stop-color:${COLORS.woolMid}"/>
      <stop offset="50%" style="stop-color:${COLORS.woolShade}"/>
      <stop offset="100%" style="stop-color:${COLORS.woolDark}"/>
    </linearGradient>
  </defs>
  <rect width="100%" height="100%" fill="transparent"/>
`;

  frames.forEach((frameData, index) => {
    const col = index % TILES_X;
    const row = Math.floor(index / TILES_X);
    const x = col * TILE_SIZE;
    const y = row * TILE_SIZE;

    svg += generate3DSheepSVG(x, y, frameData.frame, frameData.flip, index);
  });

  svg += '</svg>';
  return svg;
}

function generate3DSheepSVG(x, y, frame, flip, index) {
  const cx = x + TILE_SIZE / 2;
  const cy = y + TILE_SIZE / 2 + 4; // Shift down slightly for better centering

  const legOffset = Math.sin(frame * Math.PI / 2) * 4;
  const headBob = Math.sin(frame * Math.PI / 4) * 2;
  const isEating = frame >= 100 && frame < 200;
  const isSleeping = frame >= 200 && frame < 300;
  const isJumping = frame >= 300;

  let group = `  <g id="frame-${index}" transform="${flip ? `translate(${x + TILE_SIZE}, 0) scale(-1, 1) translate(${-x}, 0)` : ''}">
`;

  const jumpOffset = isJumping ? -10 : 0;
  const bodyY = cy - 6 + jumpOffset;

  // === BACK LEGS (behind body) ===
  const backLegX1 = cx - 12;
  const backLegX2 = cx + 6;
  const legTopY = bodyY + 14;

  // Back left leg
  group += `    <rect fill="${COLORS.woolDark}" x="${backLegX1}" y="${legTopY}" width="7" height="${14 - legOffset}" rx="1"/>
`;
  group += `    <rect fill="${COLORS.woolShade}" x="${backLegX1 + 1}" y="${legTopY}" width="4" height="${14 - legOffset - 2}" rx="1"/>
`;
  // Hoof
  group += `    <rect fill="${COLORS.hoof}" x="${backLegX1}" y="${legTopY + 11 - legOffset}" width="7" height="3" rx="1"/>
`;
  group += `    <rect fill="${COLORS.hoofHighlight}" x="${backLegX1 + 1}" y="${legTopY + 11 - legOffset}" width="3" height="2" rx="1"/>
`;

  // Back right leg
  group += `    <rect fill="${COLORS.woolDark}" x="${backLegX2}" y="${legTopY}" width="7" height="${14 + legOffset}" rx="1"/>
`;
  group += `    <rect fill="${COLORS.woolShade}" x="${backLegX2 + 1}" y="${legTopY}" width="4" height="${14 + legOffset - 2}" rx="1"/>
`;
  // Hoof
  group += `    <rect fill="${COLORS.hoof}" x="${backLegX2}" y="${legTopY + 11 + legOffset}" width="7" height="3" rx="1"/>
`;
  group += `    <rect fill="${COLORS.hoofHighlight}" x="${backLegX2 + 1}" y="${legTopY + 11 + legOffset}" width="3" height="2" rx="1"/>
`;

  // === BODY (3D wool cube) ===
  // Shadow underneath
  group += `    <ellipse fill="${COLORS.woolDark}" cx="${cx}" cy="${bodyY + 20}" rx="18" ry="4" opacity="0.3"/>
`;

  // Main body - layered for 3D effect
  // Bottom dark layer
  group += `    <rect fill="${COLORS.woolShade}" x="${cx - 18}" y="${bodyY + 2}" width="36" height="16" rx="4"/>
`;
  // Middle layer
  group += `    <rect fill="${COLORS.woolMid}" x="${cx - 17}" y="${bodyY + 1}" width="34" height="14" rx="4"/>
`;
  // Top highlight layer
  group += `    <rect fill="${COLORS.wool}" x="${cx - 16}" y="${bodyY}" width="32" height="12" rx="3"/>
`;
  // Highlight strip on top
  group += `    <rect fill="${COLORS.woolHighlight}" x="${cx - 14}" y="${bodyY + 1}" width="20" height="4" rx="2" opacity="0.7"/>
`;

  // Fluffy wool bumps on top (3D texture)
  for (let i = 0; i < 5; i++) {
    const bumpX = cx - 12 + i * 6;
    const bumpY = bodyY - 2 + Math.sin(i * 1.2) * 2;
    group += `    <ellipse fill="${COLORS.wool}" cx="${bumpX}" cy="${bumpY + 2}" rx="4" ry="3"/>
`;
    group += `    <ellipse fill="${COLORS.woolHighlight}" cx="${bumpX - 1}" cy="${bumpY + 1}" rx="2" ry="1.5" opacity="0.6"/>
`;
  }

  // Side wool fluff
  group += `    <ellipse fill="${COLORS.woolMid}" cx="${cx - 16}" cy="${bodyY + 8}" rx="4" ry="6"/>
`;
  group += `    <ellipse fill="${COLORS.woolShade}" cx="${cx + 16}" cy="${bodyY + 8}" rx="4" ry="6"/>
`;

  // === FRONT LEGS ===
  const frontLegX1 = cx - 8;
  const frontLegX2 = cx + 2;

  // Front left leg
  group += `    <rect fill="${COLORS.woolShade}" x="${frontLegX1}" y="${legTopY}" width="7" height="${14 + legOffset}" rx="1"/>
`;
  group += `    <rect fill="${COLORS.woolMid}" x="${frontLegX1 + 1}" y="${legTopY}" width="4" height="${14 + legOffset - 2}" rx="1"/>
`;
  group += `    <rect fill="${COLORS.wool}" x="${frontLegX1 + 2}" y="${legTopY}" width="2" height="${14 + legOffset - 4}" rx="1" opacity="0.5"/>
`;
  // Hoof
  group += `    <rect fill="${COLORS.hoof}" x="${frontLegX1}" y="${legTopY + 11 + legOffset}" width="7" height="3" rx="1"/>
`;
  group += `    <rect fill="${COLORS.hoofHighlight}" x="${frontLegX1 + 1}" y="${legTopY + 11 + legOffset}" width="3" height="2" rx="1"/>
`;

  // Front right leg
  group += `    <rect fill="${COLORS.woolShade}" x="${frontLegX2}" y="${legTopY}" width="7" height="${14 - legOffset}" rx="1"/>
`;
  group += `    <rect fill="${COLORS.woolMid}" x="${frontLegX2 + 1}" y="${legTopY}" width="4" height="${14 - legOffset - 2}" rx="1"/>
`;
  group += `    <rect fill="${COLORS.wool}" x="${frontLegX2 + 2}" y="${legTopY}" width="2" height="${14 - legOffset - 4}" rx="1" opacity="0.5"/>
`;
  // Hoof
  group += `    <rect fill="${COLORS.hoof}" x="${frontLegX2}" y="${legTopY + 11 - legOffset}" width="7" height="3" rx="1"/>
`;
  group += `    <rect fill="${COLORS.hoofHighlight}" x="${frontLegX2 + 1}" y="${legTopY + 11 - legOffset}" width="3" height="2" rx="1"/>
`;

  // === HEAD ===
  const headY = isSleeping ? bodyY - 2 : (isEating ? bodyY + 8 : bodyY - 10 + headBob);
  const headX = cx + 14;

  // Neck connection (wool)
  group += `    <ellipse fill="${COLORS.woolMid}" cx="${cx + 8}" cy="${bodyY + 2}" rx="6" ry="5"/>
`;

  // Head wool (back/top of head) - 3D cube style
  group += `    <rect fill="${COLORS.woolShade}" x="${headX - 7}" y="${headY - 6}" width="16" height="10" rx="3"/>
`;
  group += `    <rect fill="${COLORS.woolMid}" x="${headX - 6}" y="${headY - 7}" width="14" height="9" rx="3"/>
`;
  group += `    <rect fill="${COLORS.wool}" x="${headX - 5}" y="${headY - 8}" width="12" height="8" rx="2"/>
`;
  group += `    <rect fill="${COLORS.woolHighlight}" x="${headX - 3}" y="${headY - 7}" width="6" height="3" rx="1" opacity="0.6"/>
`;

  // Fluffy wool on head
  group += `    <ellipse fill="${COLORS.wool}" cx="${headX}" cy="${headY - 8}" rx="5" ry="3"/>
`;
  group += `    <ellipse fill="${COLORS.woolHighlight}" cx="${headX - 1}" cy="${headY - 9}" rx="3" ry="2" opacity="0.5"/>
`;

  // Face (3D pink cube)
  group += `    <rect fill="${COLORS.skinShade}" x="${headX - 5}" y="${headY + 1}" width="14" height="13" rx="2"/>
`;
  group += `    <rect fill="${COLORS.skin}" x="${headX - 4}" y="${headY}" width="12" height="12" rx="2"/>
`;
  group += `    <rect fill="${COLORS.skinHighlight}" x="${headX - 3}" y="${headY + 1}" width="8" height="4" rx="1" opacity="0.5"/>
`;

  // Eyes
  if (!isSleeping) {
    // Left eye
    group += `    <rect fill="${COLORS.eye}" x="${headX}" y="${headY + 3}" width="4" height="4" rx="1"/>
`;
    group += `    <rect fill="${COLORS.eyeHighlight}" x="${headX + 1}" y="${headY + 4}" width="1" height="1"/>
`;
    // Right eye
    group += `    <rect fill="${COLORS.eye}" x="${headX + 5}" y="${headY + 3}" width="4" height="4" rx="1"/>
`;
    group += `    <rect fill="${COLORS.eyeHighlight}" x="${headX + 6}" y="${headY + 4}" width="1" height="1"/>
`;
  } else {
    // Sleeping eyes (curved lines)
    group += `    <path d="M${headX + 1} ${headY + 5} Q${headX + 2.5} ${headY + 6} ${headX + 4} ${headY + 5}" stroke="${COLORS.eye}" stroke-width="1.5" fill="none"/>
`;
    group += `    <path d="M${headX + 6} ${headY + 5} Q${headX + 7.5} ${headY + 6} ${headX + 9} ${headY + 5}" stroke="${COLORS.eye}" stroke-width="1.5" fill="none"/>
`;
  }

  // Nose (3D)
  group += `    <rect fill="${COLORS.skinShade}" x="${headX + 2}" y="${headY + 8}" width="5" height="3" rx="1"/>
`;
  group += `    <rect fill="${COLORS.nose}" x="${headX + 2}" y="${headY + 7}" width="5" height="3" rx="1"/>
`;
  group += `    <rect fill="${COLORS.noseHighlight}" x="${headX + 3}" y="${headY + 7}" width="2" height="1" rx="0.5" opacity="0.6"/>
`;

  // Ears (3D with depth)
  // Left ear
  group += `    <ellipse fill="${COLORS.skinShade}" cx="${headX - 6}" cy="${headY - 1}" rx="4" ry="5"/>
`;
  group += `    <ellipse fill="${COLORS.skin}" cx="${headX - 6}" cy="${headY - 2}" rx="3" ry="4"/>
`;
  group += `    <ellipse fill="${COLORS.skinHighlight}" cx="${headX - 6}" cy="${headY - 3}" rx="1.5" ry="2" opacity="0.4"/>
`;

  // Right ear
  group += `    <ellipse fill="${COLORS.skinShade}" cx="${headX + 12}" cy="${headY - 1}" rx="4" ry="5"/>
`;
  group += `    <ellipse fill="${COLORS.skin}" cx="${headX + 12}" cy="${headY - 2}" rx="3" ry="4"/>
`;
  group += `    <ellipse fill="${COLORS.skinHighlight}" cx="${headX + 12}" cy="${headY - 3}" rx="1.5" ry="2" opacity="0.4"/>
`;

  // Grass (eating animation)
  if (isEating) {
    group += `    <rect fill="${COLORS.grass}" x="${headX + 6}" y="${headY + 11}" width="10" height="5" rx="1"/>
`;
    group += `    <rect fill="${COLORS.grassLight}" x="${headX + 7}" y="${headY + 11}" width="4" height="3" rx="1"/>
`;
    // Grass blades
    group += `    <rect fill="${COLORS.grass}" x="${headX + 8}" y="${headY + 7}" width="2" height="5" rx="1"/>
`;
    group += `    <rect fill="${COLORS.grass}" x="${headX + 12}" y="${headY + 8}" width="2" height="4" rx="1"/>
`;
  }

  // Tail (fluffy pom)
  const tailX = cx - 20;
  const tailY = bodyY + 4;
  group += `    <ellipse fill="${COLORS.woolShade}" cx="${tailX}" cy="${tailY + 1}" rx="5" ry="4"/>
`;
  group += `    <ellipse fill="${COLORS.wool}" cx="${tailX}" cy="${tailY}" rx="4" ry="3"/>
`;
  group += `    <ellipse fill="${COLORS.woolHighlight}" cx="${tailX - 1}" cy="${tailY - 1}" rx="2" ry="1.5" opacity="0.5"/>
`;

  group += `  </g>
`;
  return group;
}

// Generate and save the SVG
const svg = generateSVG();
const outputPath = path.join(__dirname, '..', 'static', 'minecraft-sheep-sprite.svg');
fs.writeFileSync(outputPath, svg);
console.log(`Generated 3D sprite sheet: ${outputPath}`);
console.log(`Dimensions: ${WIDTH}x${HEIGHT} (${TILES_X}x${TILES_Y} tiles of ${TILE_SIZE}x${TILE_SIZE})`);
