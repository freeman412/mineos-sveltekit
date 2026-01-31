//go:build !windows

package main

// ownsConsole always returns false on non-Windows platforms.
// Unix terminals stay open after a program exits, so no pause is needed.
func ownsConsole() bool {
	return false
}
