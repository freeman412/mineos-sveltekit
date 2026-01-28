package main

import (
	"fmt"
	"os"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/app"
)

func main() {
	application, err := app.New()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}

	if err := application.Run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
