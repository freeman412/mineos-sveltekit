package commands

import (
	"fmt"

	"github.com/spf13/cobra"
)

func NewVersionCommand(version string) *cobra.Command {
	return &cobra.Command{
		Use:   "version",
		Short: "Show version",
		Run: func(cmd *cobra.Command, _ []string) {
			fmt.Printf("mineos %s\n", version)
		},
	}
}
