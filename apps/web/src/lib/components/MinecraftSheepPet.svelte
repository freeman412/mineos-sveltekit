<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { browser } from '$app/environment';

	// Sheep state
	let x = $state(100);
	let y = $state(0);
	let velocityX = $state(1);
	let velocityY = $state(0);
	let frame = $state(0);
	let direction = $state<'left' | 'right'>('right');
	let action = $state<'idle' | 'walk' | 'sleep' | 'fall' | 'eat' | 'drag'>('idle');
	let isDragging = $state(false);
	let isVisible = $state(true);
	let dragOffset = { x: 0, y: 0 };

	// Animation timing
	let animationFrame: number;
	let lastTime = 0;
	const FRAME_DURATION = 150; // ms per animation frame
	const TILE_SIZE = 64;

	// Screen bounds
	let screenWidth = $state(browser ? window.innerWidth : 1920);
	let screenHeight = $state(browser ? window.innerHeight : 1080);

	// Frame indices for each animation (column in sprite sheet)
	const FRAMES = {
		idle: [0, 1, 0, 1],
		walk: [4, 5, 6, 7],
		walkLeft: [8, 9, 10, 11],
		fall: [12, 13],
		fallLeft: [14, 15],
		eat: [16, 17],
		eatLeft: [18, 19],
		sleep: [20, 21],
		drag: [28, 29],
		dragLeft: [30, 31]
	};

	// Calculate sprite position
	function getSpritePosition(frameIndex: number): { x: number; y: number } {
		const col = frameIndex % 8;
		const row = Math.floor(frameIndex / 8);
		return { x: -col * TILE_SIZE, y: -row * TILE_SIZE };
	}

	// Get current frame based on action and direction
	function getCurrentFrames(): number[] {
		switch (action) {
			case 'walk':
				return direction === 'right' ? FRAMES.walk : FRAMES.walkLeft;
			case 'fall':
				return direction === 'right' ? FRAMES.fall : FRAMES.fallLeft;
			case 'eat':
				return direction === 'right' ? FRAMES.eat : FRAMES.eatLeft;
			case 'sleep':
				return FRAMES.sleep;
			case 'drag':
				return direction === 'right' ? FRAMES.drag : FRAMES.dragLeft;
			default:
				return FRAMES.idle;
		}
	}

	// Physics and AI update
	function update(timestamp: number) {
		if (!browser) return;

		const deltaTime = timestamp - lastTime;

		if (deltaTime >= FRAME_DURATION) {
			lastTime = timestamp;
			const frames = getCurrentFrames();
			frame = (frame + 1) % frames.length;
		}

		if (!isDragging) {
			// Apply gravity when falling
			if (action === 'fall') {
				velocityY += 0.5; // gravity
				y += velocityY;

				// Check if landed
				if (y >= screenHeight - TILE_SIZE) {
					y = screenHeight - TILE_SIZE;
					velocityY = 0;
					action = 'idle';
					// Random chance to start walking
					if (Math.random() > 0.5) {
						action = 'walk';
						velocityX = direction === 'right' ? 1.5 : -1.5;
					}
				}
			} else if (action === 'walk') {
				// Walking logic
				x += velocityX;

				// Bounce off edges
				if (x <= 0) {
					x = 0;
					direction = 'right';
					velocityX = 1.5;
				} else if (x >= screenWidth - TILE_SIZE) {
					x = screenWidth - TILE_SIZE;
					direction = 'left';
					velocityX = -1.5;
				}

				// Random chance to change action
				if (Math.random() < 0.002) {
					const rand = Math.random();
					if (rand < 0.3) {
						action = 'idle';
					} else if (rand < 0.5) {
						action = 'eat';
					} else if (rand < 0.55) {
						action = 'sleep';
					} else {
						// Change direction
						direction = direction === 'right' ? 'left' : 'right';
						velocityX = direction === 'right' ? 1.5 : -1.5;
					}
				}
			} else if (action === 'idle' || action === 'eat' || action === 'sleep') {
				// Random chance to start walking
				const wakeChance = action === 'sleep' ? 0.0005 : 0.005;
				if (Math.random() < wakeChance) {
					action = 'walk';
					direction = Math.random() > 0.5 ? 'right' : 'left';
					velocityX = direction === 'right' ? 1.5 : -1.5;
				}
			}
		}

		animationFrame = requestAnimationFrame(update);
	}

	// Drag handlers
	function handleMouseDown(e: MouseEvent) {
		isDragging = true;
		action = 'drag';
		dragOffset = {
			x: e.clientX - x,
			y: e.clientY - y
		};
		e.preventDefault();
	}

	function handleMouseMove(e: MouseEvent) {
		if (isDragging) {
			x = e.clientX - dragOffset.x;
			y = e.clientY - dragOffset.y;
			// Update direction based on movement
			if (e.movementX > 0) direction = 'right';
			else if (e.movementX < 0) direction = 'left';
		}
	}

	function handleMouseUp() {
		if (isDragging) {
			isDragging = false;
			// Start falling
			action = 'fall';
			velocityY = 0;
		}
	}

	// Touch handlers for mobile
	function handleTouchStart(e: TouchEvent) {
		const touch = e.touches[0];
		isDragging = true;
		action = 'drag';
		dragOffset = {
			x: touch.clientX - x,
			y: touch.clientY - y
		};
		e.preventDefault();
	}

	function handleTouchMove(e: TouchEvent) {
		if (isDragging) {
			const touch = e.touches[0];
			const prevX = x;
			x = touch.clientX - dragOffset.x;
			y = touch.clientY - dragOffset.y;
			if (x > prevX) direction = 'right';
			else if (x < prevX) direction = 'left';
		}
	}

	function handleTouchEnd() {
		if (isDragging) {
			isDragging = false;
			action = 'fall';
			velocityY = 0;
		}
	}

	function handleResize() {
		screenWidth = window.innerWidth;
		screenHeight = window.innerHeight;
		// Keep sheep in bounds
		if (x > screenWidth - TILE_SIZE) x = screenWidth - TILE_SIZE;
		if (y > screenHeight - TILE_SIZE) y = screenHeight - TILE_SIZE;
	}

	// Double-click to toggle visibility (hide/show pet)
	function handleDoubleClick() {
		isVisible = !isVisible;
	}

	onMount(() => {
		if (!browser) return;

		// Start at bottom of screen, random x position
		x = Math.random() * (screenWidth - TILE_SIZE);
		y = screenHeight - TILE_SIZE;
		action = 'walk';
		direction = Math.random() > 0.5 ? 'right' : 'left';
		velocityX = direction === 'right' ? 1.5 : -1.5;

		// Add global event listeners
		window.addEventListener('mousemove', handleMouseMove);
		window.addEventListener('mouseup', handleMouseUp);
		window.addEventListener('touchmove', handleTouchMove, { passive: false });
		window.addEventListener('touchend', handleTouchEnd);
		window.addEventListener('resize', handleResize);

		// Start animation loop
		animationFrame = requestAnimationFrame(update);
	});

	onDestroy(() => {
		if (!browser) return;

		cancelAnimationFrame(animationFrame);
		window.removeEventListener('mousemove', handleMouseMove);
		window.removeEventListener('mouseup', handleMouseUp);
		window.removeEventListener('touchmove', handleTouchMove);
		window.removeEventListener('touchend', handleTouchEnd);
		window.removeEventListener('resize', handleResize);
	});

	// Calculate current sprite offset
	const spritePos = $derived(getSpritePosition(getCurrentFrames()[frame]));
</script>

{#if isVisible}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<button
		class="minecraft-sheep"
		style="left: {x}px; top: {y}px; background-position: {spritePos.x}px {spritePos.y}px;"
		onmousedown={handleMouseDown}
		ontouchstart={handleTouchStart}
		ondblclick={handleDoubleClick}
		aria-label="Minecraft Sheep Pet - Drag me around! Double-click to hide."
		title="Drag me around! Double-click to hide."
	></button>
{/if}

<style>
	.minecraft-sheep {
		position: fixed;
		width: 64px;
		height: 64px;
		background-image: url('/minecraft-sheep-sprite.svg');
		background-repeat: no-repeat;
		background-color: transparent;
		border: none;
		padding: 0;
		cursor: grab;
		z-index: 9999;
		image-rendering: pixelated;
		pointer-events: auto;
		user-select: none;
		-webkit-user-select: none;
		filter: drop-shadow(2px 2px 2px rgba(0, 0, 0, 0.3));
	}

	.minecraft-sheep:active {
		cursor: grabbing;
	}

	.minecraft-sheep:focus {
		outline: none;
	}
</style>
