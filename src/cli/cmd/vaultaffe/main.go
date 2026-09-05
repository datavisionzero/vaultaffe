// Command vaultaffe is the CLI of Specification §6.2 — `login`, `setup`, `run`
// and the secrets surface. None of that is here yet: the command tree, its exit
// codes and the directory binding arrive with the CLI skeleton, and this file
// is what makes the module a buildable subject for CI in the meantime.
package main

import (
	"fmt"
	"os"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/version"
)

func main() {
	fmt.Fprintf(os.Stdout, "vaultaffe %s\n", version.Value)
}
