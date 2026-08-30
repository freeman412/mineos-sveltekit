import { describe, expect, it } from 'vitest';
import { describeCron } from './cron';

describe('describeCron', () => {
	it('renders interval expressions without a timezone', () => {
		expect(describeCron('0 * * * *')).toBe('Every hour');
		expect(describeCron('0 */6 * * *')).toBe('Every 6 hours');
		expect(describeCron('0 */6 * * *', 'local', 120)).toBe('Every 6 hours');
	});

	it('renders daily and weekly schedules in UTC by default', () => {
		expect(describeCron('0 3 * * *')).toBe('Daily at 03:00 UTC');
		expect(describeCron('30 2 * * 0')).toBe('Every Sunday at 02:30 UTC');
	});

	it('shifts daily schedules into local time', () => {
		// UTC+2: 03:00 UTC -> 05:00 local
		expect(describeCron('0 3 * * *', 'local', 120)).toBe('Daily at 05:00 local time');
		// UTC-5: 03:00 UTC -> 22:00 local (previous day, still "daily")
		expect(describeCron('0 3 * * *', 'local', -300)).toBe('Daily at 22:00 local time');
		// Half-hour offset (UTC+5:30)
		expect(describeCron('0 3 * * *', 'local', 330)).toBe('Daily at 08:30 local time');
	});

	it('shifts the weekday when a weekly schedule crosses midnight', () => {
		// Sunday 22:00 UTC at UTC+3 -> Monday 01:00 local
		expect(describeCron('0 22 * * 0', 'local', 180)).toBe('Every Monday at 01:00 local time');
		// Sunday 02:00 UTC at UTC-5 -> Saturday 21:00 local
		expect(describeCron('0 2 * * 0', 'local', -300)).toBe('Every Saturday at 21:00 local time');
		// No rollover: Wednesday 12:00 UTC at UTC+2 -> Wednesday 14:00
		expect(describeCron('0 12 * * 3', 'local', 120)).toBe('Every Wednesday at 14:00 local time');
	});

	it('keeps UTC rendering for unshiftable expressions in local mode', () => {
		// Hour lists cannot be shifted per-entry; historical rendering preserved.
		expect(describeCron('0 0,12 * * *', 'local', 120)).toBe('Daily at 0,12:00 UTC');
		// Day-of-week ranges fall back to UTC too.
		expect(describeCron('30 2 * * 1-5', 'local', 120)).toBe('Day 1-5 at 02:30 UTC');
	});

	it('flags invalid expressions and passes through unrecognized ones', () => {
		expect(describeCron('0 3 * *')).toBe('Invalid expression');
		expect(describeCron('*/5 * * * *')).toBe('*/5 * * * *');
	});
});
