package cmd

import (
	"bytes"
	"fmt"
	"io"
	"os"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
)

// readValue reads a value from stdin, and only from stdin. It is never an
// argument: an argument is in the shell history and in `ps`, and Doppler's
// answer to that is five lines of HISTIGNORE — a design explaining its own
// failure (Specification §6.2).
//
// Exactly one trailing newline is removed, because practically every command
// ends its output with one and a key carrying an invisible `\n` is the bug that
// costs an afternoon. `--raw` keeps the bytes as they came. A multi-line value —
// a PEM key — passes through untouched but for that one byte.
func readValue(in io.Reader, raw bool) (string, error) {
	if in == nil {
		in = os.Stdin
	}

	content, err := io.ReadAll(in)
	if err != nil {
		return "", &config.UsageError{Message: fmt.Sprintf("the value could not be read from stdin: %v", err)}
	}
	if raw {
		return string(content), nil
	}
	return string(trimOneNewline(content)), nil
}

// trimOneNewline removes one trailing "\n" and the "\r" in front of it if there
// is one — a value that came off a Windows pipe is not a value with a stray
// carriage return in it. Exactly one: two blank lines at the end of a PEM file
// were put there by whoever wrote it.
func trimOneNewline(content []byte) []byte {
	if !bytes.HasSuffix(content, []byte("\n")) {
		return content
	}
	content = content[:len(content)-1]
	if bytes.HasSuffix(content, []byte("\r")) {
		content = content[:len(content)-1]
	}
	return content
}

func names(environments []api.Environment) []string {
	found := make([]string, 0, len(environments))
	for _, environment := range environments {
		found = append(found, environment.Name)
	}
	return found
}
