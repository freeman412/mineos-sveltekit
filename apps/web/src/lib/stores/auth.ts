import { writable } from 'svelte/store';

export type AuthUser = {
	username: string;
	role: string;
};

function createAuthStore() {
	const { subscribe, set, update } = writable<AuthUser | null>(null);

	return {
		subscribe,
		setUser: (user: AuthUser | null) => set(user),
		logout: () => set(null)
	};
}

export const authUser = createAuthStore();
