//go:build !windows

package commands

import "os"

func isRoot() bool {
	return os.Geteuid() == 0
}
