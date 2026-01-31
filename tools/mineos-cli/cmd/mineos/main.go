package main

import (
	"bufio"
	"fmt"
	"os"
	"runtime"

	"golang.org/x/term"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/app"
)

func main() {
	application, err := app.New()
	if err != nil {
		exitWithError(err)
	}

	if err := application.Run(); err != nil {
		exitWithError(err)
	}
}

// exitWithError prints the error and pauses on Windows if the console
// would close (e.g., double-clicking the exe from Explorer).
func exitWithError(err error) {
	fmt.Fprintln(os.Stderr, err)

	if runtime.GOOS == "windows" && term.IsTerminal(int(os.Stdin.Fd())) {
		// Check if we own the console (double-clicked from Explorer).
		// If the user ran from an existing terminal, they can already see the error.
		// We detect this by checking if the parent allocated our console.
		if ownsConsole() {
			fmt.Fprintln(os.Stderr, "")
			fmt.Fprintln(os.Stderr, "Press Enter to close...")
			bufio.NewReader(os.Stdin).ReadBytes('\n')
		}
	}

	os.Exit(1)
}
