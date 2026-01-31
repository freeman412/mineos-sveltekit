package main

import (
	"syscall"
	"unsafe"
)

var (
	kernel32                  = syscall.NewLazyDLL("kernel32.dll")
	procGetConsoleProcessList = kernel32.NewProc("GetConsoleProcessList")
)

// ownsConsole returns true when this process is the only one attached to its
// console — i.e., the user double-clicked the exe from Explorer rather than
// running it from an existing terminal window.
func ownsConsole() bool {
	pids := make([]uint32, 16)
	r, _, _ := procGetConsoleProcessList.Call(
		uintptr(unsafe.Pointer(&pids[0])),
		uintptr(len(pids)),
	)
	// r == number of PIDs attached to this console.
	// 1 means only us → console will close when we exit.
	return r <= 1
}
