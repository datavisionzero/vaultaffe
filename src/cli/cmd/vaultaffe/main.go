// Command vaultaffe is the CLI of Specification §6.2: `login`, `setup`, `run`
// and the secrets surface. It is a client of the public HTTP API and nothing
// else — one static binary, on a laptop, a CI runner or inside an agent's
// container.
package main

import (
	"context"
	"os"
	"os/signal"
	"syscall"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/cmd"
)

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()

	os.Exit(cmd.Run(ctx, os.Args[1:], cmd.Env{
		Getenv:  os.Getenv,
		Stdin:   os.Stdin,
		Stdout:  os.Stdout,
		Stderr:  os.Stderr,
		Environ: os.Environ,
		// `run` replaces this process with the one it was asked to start
		// (ADR 0009). There is no wrapper, no signal forwarding and nothing to
		// do afterwards, because afterwards this process does not exist.
		Exec: syscall.Exec,
	}))
}
