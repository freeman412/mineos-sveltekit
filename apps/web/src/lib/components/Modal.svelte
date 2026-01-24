<script lang="ts">
	import { modalStore, type ModalType } from '$lib/stores/modal';
	import { fade, scale } from 'svelte/transition';

	function handleConfirm() {
		$modalStore.onConfirm?.();
	}

	function handleCancel() {
		$modalStore.onCancel?.();
	}

	function handleBackdropClick(e: MouseEvent) {
		if (e.target === e.currentTarget) {
			if ($modalStore.type === 'confirm') {
				handleCancel();
			} else {
				handleConfirm();
			}
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (!$modalStore.isOpen) return;

		if (e.key === 'Escape') {
			if ($modalStore.type === 'confirm') {
				handleCancel();
			} else {
				handleConfirm();
			}
		} else if (e.key === 'Enter') {
			handleConfirm();
		}
	}

	function getIcon(type: ModalType): string {
		switch (type) {
			case 'error':
				return '!';
			case 'success':
				return '\u2713';
			case 'confirm':
				return '?';
			default:
				return 'i';
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

{#if $modalStore.isOpen}
	<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
	<div
		class="modal-backdrop"
		transition:fade={{ duration: 150 }}
		onclick={handleBackdropClick}
		role="presentation"
	>
		<div
			class="modal-container"
			transition:scale={{ duration: 150, start: 0.95 }}
			role="dialog"
			aria-modal="true"
			aria-labelledby="modal-title"
			tabindex="-1"
		>
			<div class="modal-icon {$modalStore.type}">
				<span>{getIcon($modalStore.type)}</span>
			</div>

			<h2 id="modal-title" class="modal-title">{$modalStore.title}</h2>

			<p class="modal-message">{$modalStore.message}</p>

			<div class="modal-actions" class:single={$modalStore.type !== 'confirm'}>
				{#if $modalStore.type === 'confirm'}
					<button class="btn btn-secondary" onclick={handleCancel}>
						{$modalStore.cancelText}
					</button>
				{/if}
				<button
					class="btn btn-primary {$modalStore.type}"
					onclick={handleConfirm}
				>
					{$modalStore.confirmText}
				</button>
			</div>
		</div>
	</div>
{/if}

<style>
	.modal-backdrop {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.7);
		backdrop-filter: blur(4px);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 9999;
		padding: 20px;
	}

	.modal-container {
		background: #1a1e2f;
		border: 1px solid #2a2f47;
		border-radius: 16px;
		padding: 32px;
		max-width: 420px;
		width: 100%;
		text-align: center;
		box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
	}

	.modal-icon {
		width: 56px;
		height: 56px;
		border-radius: 50%;
		display: flex;
		align-items: center;
		justify-content: center;
		margin: 0 auto 20px;
		font-size: 24px;
		font-weight: 700;
	}

	.modal-icon.alert {
		background: rgba(59, 130, 246, 0.15);
		color: #3b82f6;
		border: 2px solid rgba(59, 130, 246, 0.3);
	}

	.modal-icon.error {
		background: rgba(239, 68, 68, 0.15);
		color: #ef4444;
		border: 2px solid rgba(239, 68, 68, 0.3);
	}

	.modal-icon.success {
		background: rgba(34, 197, 94, 0.15);
		color: #22c55e;
		border: 2px solid rgba(34, 197, 94, 0.3);
	}

	.modal-icon.confirm {
		background: rgba(251, 191, 36, 0.15);
		color: #fbbf24;
		border: 2px solid rgba(251, 191, 36, 0.3);
	}

	.modal-title {
		margin: 0 0 12px;
		font-size: 20px;
		font-weight: 600;
		color: #eef0f8;
	}

	.modal-message {
		margin: 0 0 28px;
		font-size: 15px;
		line-height: 1.6;
		color: #9aa2c5;
	}

	.modal-actions {
		display: flex;
		gap: 12px;
		justify-content: center;
	}

	.modal-actions.single {
		justify-content: center;
	}

	.btn {
		padding: 12px 28px;
		border-radius: 10px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: all 0.2s;
		border: none;
		font-family: inherit;
		min-width: 100px;
	}

	.btn-secondary {
		background: #2a2f47;
		color: #9aa2c5;
	}

	.btn-secondary:hover {
		background: #3a3f5a;
		color: #eef0f8;
	}

	.btn-primary {
		background: #5865f2;
		color: #fff;
	}

	.btn-primary:hover {
		background: #4752c4;
	}

	.btn-primary.error {
		background: #ef4444;
	}

	.btn-primary.error:hover {
		background: #dc2626;
	}

	.btn-primary.success {
		background: #22c55e;
	}

	.btn-primary.success:hover {
		background: #16a34a;
	}

	.btn-primary.confirm {
		background: #5865f2;
	}

	@media (max-width: 480px) {
		.modal-container {
			padding: 24px;
		}

		.modal-actions {
			flex-direction: column-reverse;
		}

		.btn {
			width: 100%;
		}
	}
</style>
