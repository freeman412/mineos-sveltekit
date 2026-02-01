<script lang="ts">
	import { theme } from '$lib/stores/uiPreferences';

	let isAnimating = false;

	function toggleTheme() {
		isAnimating = true;
		theme.toggle();

		// Reset animation after it completes
		setTimeout(() => {
			isAnimating = false;
		}, 800);
	}

	$: isNether = $theme === 'nether';
</script>

<button
	class="portal-button"
	class:nether={isNether}
	class:animating={isAnimating}
	onclick={toggleTheme}
	aria-label={isNether ? 'Return to Overworld' : 'Enter the Nether'}
	title={isNether ? 'Return to Overworld' : 'Enter the Nether'}
>
	<svg
		xmlns="http://www.w3.org/2000/svg"
		viewBox="0 0 24 24"
		fill="none"
		stroke="currentColor"
		stroke-width="2"
		stroke-linecap="round"
		stroke-linejoin="round"
		class="portal-icon"
		class:nether={isNether}
	>
		<!-- Obsidian frame (outer rectangle) -->
		<rect x="3" y="2" width="18" height="20" rx="1" class="obsidian-frame" />

		<!-- Inner portal area -->
		<rect x="5" y="4" width="14" height="16" class="portal-area" />

		<!-- Portal swirl effect lines -->
		<path d="M8 8 Q12 12 8 16" class="portal-swirl swirl-1" />
		<path d="M12 8 Q15 12 12 16" class="portal-swirl swirl-2" />
		<path d="M16 8 Q12 12 16 16" class="portal-swirl swirl-3" />
	</svg>

	<span class="portal-glow" class:nether={isNether}></span>

	{#if isAnimating}
		<span class="activation-ring"></span>
		<span class="activation-particles"></span>
	{/if}
</button>

<style>
	.portal-button {
		position: relative;
		background: rgba(26, 10, 31, 0.3);
		border: 1px solid rgba(138, 91, 246, 0.3);
		padding: 8px;
		cursor: pointer;
		display: flex;
		align-items: center;
		justify-content: center;
		border-radius: 8px;
		transition: all 0.3s ease;
		width: 40px;
		height: 40px;
		overflow: visible;
	}

	.portal-button.nether {
		background: rgba(69, 10, 10, 0.4);
		border-color: rgba(239, 68, 68, 0.5);
		box-shadow: 0 0 20px rgba(239, 68, 68, 0.3), inset 0 0 15px rgba(251, 146, 60, 0.2);
	}

	.portal-button:hover {
		background: rgba(138, 91, 246, 0.15);
		border-color: rgba(138, 91, 246, 0.5);
		transform: scale(1.08);
		box-shadow: 0 0 15px rgba(138, 91, 246, 0.4);
	}

	.portal-button.nether:hover {
		background: rgba(127, 29, 29, 0.5);
		border-color: rgba(239, 68, 68, 0.8);
		box-shadow: 0 0 25px rgba(239, 68, 68, 0.6), inset 0 0 20px rgba(251, 146, 60, 0.3);
	}

	.portal-button:active {
		transform: scale(0.92);
	}

	.portal-button.animating {
		animation: portalActivation 0.8s ease-out;
	}

	@keyframes portalActivation {
		0% {
			transform: scale(1);
			filter: brightness(1);
		}
		20% {
			transform: scale(1.3);
			filter: brightness(2) saturate(2);
		}
		50% {
			transform: scale(0.9);
			filter: brightness(1.5) saturate(1.5);
		}
		100% {
			transform: scale(1);
			filter: brightness(1) saturate(1);
		}
	}

	.portal-icon {
		width: 24px;
		height: 24px;
		position: relative;
		z-index: 2;
		transition: all 0.4s ease;
	}

	.portal-icon.nether {
		filter: drop-shadow(0 0 4px rgba(239, 68, 68, 0.8));
	}

	/* Obsidian frame - purple/black for overworld */
	.obsidian-frame {
		fill: #1a0a1f;
		stroke: #5b21b6;
		stroke-width: 1.8;
		transition: all 0.4s ease;
	}

	.nether .obsidian-frame {
		fill: #450a0a;
		stroke: #dc2626;
		stroke-width: 2;
	}

	/* Portal area - animated purple glow */
	.portal-area {
		fill: #8b5cf6;
		opacity: 0.8;
		animation: portalPulse 2s ease-in-out infinite;
		transition: all 0.4s ease;
	}

	.nether .portal-area {
		fill: #ef4444;
		opacity: 0.95;
		filter: drop-shadow(0 0 8px rgba(239, 68, 68, 0.8));
		animation: netherPortalPulse 1.5s ease-in-out infinite;
	}

	@keyframes portalPulse {
		0%, 100% {
			opacity: 0.6;
			filter: brightness(1);
		}
		50% {
			opacity: 0.9;
			filter: brightness(1.4);
		}
	}

	@keyframes netherPortalPulse {
		0%, 100% {
			opacity: 0.85;
			filter: brightness(1.2) drop-shadow(0 0 6px rgba(239, 68, 68, 0.6));
		}
		50% {
			opacity: 1;
			filter: brightness(1.6) drop-shadow(0 0 12px rgba(239, 68, 68, 1));
		}
	}

	/* Portal swirl lines */
	.portal-swirl {
		stroke: #a78bfa;
		stroke-width: 1.5;
		fill: none;
		opacity: 0.6;
		animation: swirlFloat 3s ease-in-out infinite;
		transition: all 0.4s ease;
	}

	.nether .portal-swirl {
		stroke: #fb923c;
		opacity: 0.9;
		animation: netherSwirlFloat 2s ease-in-out infinite;
	}

	.swirl-1 {
		animation-delay: 0s;
	}

	.swirl-2 {
		animation-delay: 0.5s;
	}

	.swirl-3 {
		animation-delay: 1s;
	}

	@keyframes swirlFloat {
		0%, 100% {
			opacity: 0.4;
			transform: translateX(0);
		}
		50% {
			opacity: 0.8;
			transform: translateX(2px);
		}
	}

	@keyframes netherSwirlFloat {
		0%, 100% {
			opacity: 0.7;
			transform: translateX(0) translateY(0);
		}
		33% {
			opacity: 1;
			transform: translateX(1px) translateY(-1px);
		}
		66% {
			opacity: 0.8;
			transform: translateX(-1px) translateY(1px);
		}
	}

	/* Portal glow background */
	.portal-glow {
		position: absolute;
		inset: -8px;
		border-radius: 12px;
		background: radial-gradient(circle, rgba(138, 91, 246, 0.4) 0%, rgba(138, 91, 246, 0.2) 40%, transparent 70%);
		pointer-events: none;
		transition: all 0.4s ease;
		z-index: 1;
		animation: glowPulse 3s ease-in-out infinite;
	}

	.portal-glow.nether {
		background: radial-gradient(circle, rgba(239, 68, 68, 0.6) 0%, rgba(251, 146, 60, 0.4) 40%, rgba(239, 68, 68, 0.2) 60%, transparent 80%);
		animation: netherGlowPulse 2s ease-in-out infinite;
	}

	@keyframes glowPulse {
		0%, 100% {
			opacity: 0.6;
			transform: scale(1);
		}
		50% {
			opacity: 1;
			transform: scale(1.1);
		}
	}

	@keyframes netherGlowPulse {
		0%, 100% {
			opacity: 0.8;
			transform: scale(1);
		}
		50% {
			opacity: 1;
			transform: scale(1.15);
		}
	}

	/* Activation ring effect */
	.activation-ring {
		position: absolute;
		inset: -4px;
		border: 3px solid currentColor;
		border-radius: 12px;
		opacity: 0;
		z-index: 3;
		animation: ringExpand 0.8s ease-out;
		color: rgba(138, 91, 246, 0.8);
	}

	.nether .activation-ring {
		color: rgba(239, 68, 68, 0.9);
	}

	@keyframes ringExpand {
		0% {
			opacity: 1;
			transform: scale(0.8);
		}
		100% {
			opacity: 0;
			transform: scale(2.5);
		}
	}

	/* Activation particles effect */
	.activation-particles {
		position: absolute;
		inset: 0;
		border-radius: 8px;
		background: radial-gradient(circle, rgba(138, 91, 246, 0.6) 0%, transparent 60%);
		opacity: 0;
		z-index: 0;
		animation: particlesBurst 0.8s ease-out;
	}

	.nether .activation-particles {
		background: radial-gradient(circle, rgba(239, 68, 68, 0.8) 0%, rgba(251, 146, 60, 0.4) 40%, transparent 70%);
	}

	@keyframes particlesBurst {
		0% {
			opacity: 1;
			transform: scale(0.5);
			filter: blur(0px);
		}
		100% {
			opacity: 0;
			transform: scale(3);
			filter: blur(20px);
		}
	}
</style>
