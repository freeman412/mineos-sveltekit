/**
 * Shared formatting utilities for consistent display across the app.
 */

const SIZE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

/**
 * Format bytes into human-readable size string.
 * @param bytes - Number of bytes
 * @param decimals - Number of decimal places (default: 2)
 * @returns Formatted string like "1.50 GB"
 */
export function formatBytes(bytes: number | null | undefined, decimals = 2): string {
	if (bytes == null || bytes === 0) return '0 B';
	if (bytes < 0) return '-' + formatBytes(-bytes, decimals);

	const k = 1024;
	const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), SIZE_UNITS.length - 1);
	const value = bytes / Math.pow(k, i);

	return `${value.toFixed(decimals)} ${SIZE_UNITS[i]}`;
}

/**
 * Alias for formatBytes for backwards compatibility.
 */
export const formatFileSize = formatBytes;

/**
 * Alias for formatBytes for backwards compatibility.
 */
export const formatSize = formatBytes;

/**
 * Format a date string into localized date/time string.
 * @param dateStr - ISO date string or Date object
 * @returns Localized date string
 */
export function formatDate(dateStr: string | Date | null | undefined): string {
	if (!dateStr) return '';
	const date = typeof dateStr === 'string' ? new Date(dateStr) : dateStr;
	if (isNaN(date.getTime())) return '';
	return date.toLocaleString();
}

/**
 * Format a date string into localized date only (no time).
 * @param dateStr - ISO date string or Date object
 * @returns Localized date string without time
 */
export function formatDateOnly(dateStr: string | Date | null | undefined): string {
	if (!dateStr) return '';
	const date = typeof dateStr === 'string' ? new Date(dateStr) : dateStr;
	if (isNaN(date.getTime())) return '';
	return date.toLocaleDateString();
}

/**
 * Format a date into relative time (e.g., "2 hours ago", "Yesterday").
 * @param dateStr - ISO date string or Date object
 * @returns Relative time string
 */
export function formatRelativeTime(dateStr: string | Date | null | undefined): string {
	if (!dateStr) return '';
	const date = typeof dateStr === 'string' ? new Date(dateStr) : dateStr;
	if (isNaN(date.getTime())) return '';

	const now = new Date();
	const diffMs = now.getTime() - date.getTime();
	const diffSecs = Math.floor(diffMs / 1000);
	const diffMins = Math.floor(diffSecs / 60);
	const diffHours = Math.floor(diffMins / 60);
	const diffDays = Math.floor(diffHours / 24);

	if (diffSecs < 60) return 'Just now';
	if (diffMins < 60) return `${diffMins} minute${diffMins !== 1 ? 's' : ''} ago`;
	if (diffHours < 24) return `${diffHours} hour${diffHours !== 1 ? 's' : ''} ago`;
	if (diffDays === 1) return 'Yesterday';
	if (diffDays < 7) return `${diffDays} days ago`;

	return formatDateOnly(date);
}

/**
 * Format uptime in seconds to human-readable string.
 * @param seconds - Total seconds of uptime
 * @returns Formatted string like "2d 5h 30m"
 */
export function formatUptime(seconds: number | null | undefined): string {
	if (seconds == null || seconds < 0) return '0s';

	const days = Math.floor(seconds / 86400);
	const hours = Math.floor((seconds % 86400) / 3600);
	const minutes = Math.floor((seconds % 3600) / 60);
	const secs = Math.floor(seconds % 60);

	const parts: string[] = [];
	if (days > 0) parts.push(`${days}d`);
	if (hours > 0) parts.push(`${hours}h`);
	if (minutes > 0) parts.push(`${minutes}m`);
	if (secs > 0 || parts.length === 0) parts.push(`${secs}s`);

	return parts.join(' ');
}

/**
 * Format a number with thousands separators.
 * @param num - Number to format
 * @returns Formatted string with commas
 */
export function formatNumber(num: number | null | undefined): string {
	if (num == null) return '0';
	return num.toLocaleString();
}

/**
 * Format a percentage value.
 * @param value - Percentage value (0-100)
 * @param decimals - Number of decimal places (default: 0)
 * @returns Formatted string like "75%"
 */
export function formatPercent(value: number | null | undefined, decimals = 0): string {
	if (value == null) return '0%';
	return `${value.toFixed(decimals)}%`;
}
