// Human-readable interpretation of the 5-field cron expressions used by
// scheduled tasks. The scheduler evaluates expressions in UTC
// (CronSchedulerService); this module only localizes the *description*.

export type CronTimeMode = 'utc' | 'local';

const DOW_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/**
 * Describe a cron expression in UTC or the viewer's local time.
 *
 * `offsetMinutes` is the local offset from UTC (east-positive, e.g. +120 for
 * UTC+2). It defaults to the browser's current offset and exists as a
 * parameter so tests are deterministic. Only expressions with a single fixed
 * hour/minute can be localized; interval and list forms are shown as-is.
 */
export function describeCron(
	expr: string,
	mode: CronTimeMode = 'utc',
	offsetMinutes: number = mode === 'local' ? -new Date().getTimezoneOffset() : 0
): string {
	const parts = expr.trim().split(/\s+/);
	if (parts.length < 5) return 'Invalid expression';

	const [min, hour, dom, mon, dow] = parts;

	if (min === '0' && hour === '*') return 'Every hour';
	if (min === '0' && hour.startsWith('*/')) return `Every ${hour.slice(2)} hours`;

	const fixedTime = /^\d+$/.test(min) && /^\d+$/.test(hour);
	const daily = min !== '*' && hour !== '*' && dow === '*' && dom === '*' && mon === '*';
	const weekly = min !== '*' && hour !== '*' && dow !== '*';

	if (!daily && !weekly) return expr;

	// Non-numeric hour/minute (lists like "0,12") cannot be shifted; keep the
	// historical UTC rendering in both modes.
	if (mode === 'utc' || !fixedTime) {
		if (daily) return `Daily at ${pad(hour)}:${pad(min)} UTC`;
		return `${describeDow(dow)} at ${pad(hour)}:${pad(min)} UTC`;
	}

	const shifted = shiftTime(parseInt(hour, 10), parseInt(min, 10), offsetMinutes);
	const time = `${pad(String(shifted.hour))}:${pad(String(shifted.min))} local time`;
	if (daily) return `Daily at ${time}`;

	if (!/^\d$/.test(dow) || parseInt(dow, 10) > 6) {
		// Ranges/lists of days can't be shifted reliably; fall back to UTC.
		return `${describeDow(dow)} at ${pad(hour)}:${pad(min)} UTC`;
	}
	const localDow = (((parseInt(dow, 10) + shifted.dayShift) % 7) + 7) % 7;
	return `Every ${DOW_NAMES[localDow]} at ${time}`;
}

/** Shift a UTC wall-clock time by an offset, reporting any day rollover. */
function shiftTime(
	hour: number,
	min: number,
	offsetMinutes: number
): { hour: number; min: number; dayShift: number } {
	const total = hour * 60 + min + offsetMinutes;
	const dayShift = Math.floor(total / 1440);
	const normalized = ((total % 1440) + 1440) % 1440;
	return { hour: Math.floor(normalized / 60), min: normalized % 60, dayShift };
}

function describeDow(dow: string): string {
	// Strict digit check: parseInt('1-5') would otherwise read as Monday.
	if (/^\d$/.test(dow) && parseInt(dow, 10) <= 6) return `Every ${DOW_NAMES[parseInt(dow, 10)]}`;
	return `Day ${dow}`;
}

function pad(value: string): string {
	return value.padStart(2, '0');
}
