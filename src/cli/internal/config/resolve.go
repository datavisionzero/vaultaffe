package config

import (
	"fmt"
	"os"
	"strings"
)

// Resolved is what a command actually runs with, and where every part of it
// came from. The provenance is not decoration: "which environment is this going
// to write to, and who said so" is the question somebody asks a second before
// they would have been sorry.
type Resolved struct {
	Address     string
	Token       string
	TokenFrom   string
	Project     string
	Environment string
	ProjectFrom string
	EnvFrom     string
}

// Input is everything Resolve reads, so that a test supplies all of it and
// nothing reaches around it to the real machine.
type Input struct {
	Getenv         func(string) string
	Dir            string
	File           File
	Address        string
	Project        string
	Environment    string
	AllowPlainHTTP bool
	ReadKeychain   func(instance string) (string, error)
}

// Environment variables, in one place because they are a contract with CI, with
// containers and with whatever harness starts an agent.
const (
	EnvURL          = "VAULTAFFE_URL"
	EnvToken        = "VAULTAFFE_TOKEN"
	EnvProject      = "VAULTAFFE_PROJECT"
	EnvEnvironment  = "VAULTAFFE_ENVIRONMENT"
	EnvInsecureHTTP = "VAULTAFFE_INSECURE_HTTP"

	// What the first run presents to claim an unstarted instance (ADR 0019).
	// Not a token and never confused with one: it authenticates nobody and
	// opens exactly one endpoint, which is why it is its own variable rather
	// than a use of VAULTAFFE_TOKEN.
	EnvClaim = "VAULTAFFE_CLAIM"
)

// FromKeychain is what ResolveToken answers as the provenance of a token it read
// out of the machine's own keychain. It is a sentence a person reads — `status`
// prints it — and also the one thing a caller can compare against to know that
// the token came from somewhere that is not a file.
const FromKeychain = "the keychain"

// Address answers which instance this invocation talks to: the flag, then the
// environment, then the instance this machine logged in to.
func (in Input) ResolveAddress() (string, error) {
	address := strings.TrimSpace(in.Address)
	if address == "" {
		address = strings.TrimSpace(in.getenv(EnvURL))
	}
	if address == "" {
		address = strings.TrimSpace(in.File.Instance)
	}
	if address == "" {
		return "", &UsageError{fmt.Sprintf(
			"no instance: set %s, or run `vaultaffe login --url https://vault.example.com`.", EnvURL)}
	}

	address = strings.TrimRight(address, "/")
	if err := CheckAddress(address, in.allowPlainHTTP()); err != nil {
		return "", err
	}
	return address, nil
}

// ResolveToken answers the token and where it came from. The environment wins,
// because that is how an agent receives its own token and how CI holds one; a
// file the user chose is next, because they chose it; the keychain is last and
// is where `login` puts a session unless it was told otherwise.
func (in Input) ResolveToken(address string) (string, string, error) {
	if token := strings.TrimSpace(in.getenv(EnvToken)); token != "" {
		return token, EnvToken, nil
	}

	if path := strings.TrimSpace(in.File.TokenFile); path != "" {
		token, err := ReadTokenFile(path)
		if err != nil {
			return "", "", err
		}
		return token, path, nil
	}

	if in.ReadKeychain != nil {
		token, err := in.ReadKeychain(address)
		if err == nil && strings.TrimSpace(token) != "" {
			return strings.TrimSpace(token), FromKeychain, nil
		}
	}

	return "", "", &UsageError{fmt.Sprintf(
		"no token for %s: run `vaultaffe login`, or put one in %s.", address, EnvToken)}
}

// ResolveBinding answers the project and the environment, each through the same
// ladder and each independently: a --project on the command line does not throw
// away the environment this directory is bound to.
func (in Input) ResolveBinding() (project, environment, projectFrom, envFrom string, err error) {
	binding, bound := in.File.BindingFor(in.Dir)

	project, projectFrom = first(
		source{strings.TrimSpace(in.Project), "--project"},
		source{strings.TrimSpace(in.getenv(EnvProject)), EnvProject},
		source{bindingValue(bound, binding.Project), "the binding for " + binding.Directory},
	)
	environment, envFrom = first(
		source{strings.TrimSpace(in.Environment), "--environment"},
		source{strings.TrimSpace(in.getenv(EnvEnvironment)), EnvEnvironment},
		source{bindingValue(bound, binding.Environment), "the binding for " + binding.Directory},
	)

	if project == "" || environment == "" {
		return "", "", "", "", &UsageError{fmt.Sprintf(
			"%s is not bound to a project and an environment: run `vaultaffe setup --project <name> --environment <name>` here, or set %s and %s.",
			in.Dir, EnvProject, EnvEnvironment)}
	}
	return project, environment, projectFrom, envFrom, nil
}

// Resolve is all three at once: what every command that talks to the instance
// about a secret needs.
func Resolve(in Input) (Resolved, error) {
	address, err := in.ResolveAddress()
	if err != nil {
		return Resolved{}, err
	}

	token, from, err := in.ResolveToken(address)
	if err != nil {
		return Resolved{}, err
	}

	project, environment, projectFrom, envFrom, err := in.ResolveBinding()
	if err != nil {
		return Resolved{Address: address, Token: token, TokenFrom: from}, err
	}

	return Resolved{
		Address:     address,
		Token:       token,
		TokenFrom:   from,
		Project:     project,
		Environment: environment,
		ProjectFrom: projectFrom,
		EnvFrom:     envFrom,
	}, nil
}

// ReadTokenFile reads a token out of the file the user chose over the keychain,
// and refuses one anybody else on the machine can read. A file mode is the only
// protection a token in a file has; a CLI that shrugged at 0644 would be the
// quiet fallback to plaintext this product refuses to make.
func ReadTokenFile(path string) (string, error) {
	info, err := os.Stat(path)
	if err != nil {
		return "", &UsageError{fmt.Sprintf("%s holds this machine's token and could not be read: %v", path, err)}
	}
	if mode := info.Mode().Perm(); mode&0o077 != 0 {
		return "", &UsageError{fmt.Sprintf(
			"%s is readable by others (mode %04o). A token in a file is protected by nothing else: `chmod 600 %s`.",
			path, mode, path)}
	}

	content, err := os.ReadFile(path)
	if err != nil {
		return "", &UsageError{fmt.Sprintf("%s could not be read: %v", path, err)}
	}

	token := strings.TrimSpace(string(content))
	if token == "" {
		return "", &UsageError{fmt.Sprintf("%s is empty: run `vaultaffe login --token-file %s`.", path, path)}
	}
	return token, nil
}

// WriteTokenFile writes one, readable by its owner and nobody else.
func WriteTokenFile(path, token string) error {
	return os.WriteFile(path, []byte(token+"\n"), 0o600)
}

func (in Input) getenv(name string) string {
	if in.Getenv == nil {
		return ""
	}
	return in.Getenv(name)
}

func (in Input) allowPlainHTTP() bool {
	if in.AllowPlainHTTP {
		return true
	}
	switch strings.TrimSpace(in.getenv(EnvInsecureHTTP)) {
	case "1", "true", "yes":
		return true
	default:
		return false
	}
}

type source struct{ value, from string }

func first(sources ...source) (string, string) {
	for _, s := range sources {
		if s.value != "" {
			return s.value, s.from
		}
	}
	return "", ""
}

func bindingValue(bound bool, value string) string {
	if !bound {
		return ""
	}
	return value
}

// ResolveProject answers the project alone, for the commands that are about a
// project rather than about something inside an environment. The environment
// half of the binding is not required and not complained about.
func (in Input) ResolveProject() (string, string, error) {
	binding, bound := in.File.BindingFor(in.Dir)

	project, from := first(
		source{strings.TrimSpace(in.Project), "--project"},
		source{strings.TrimSpace(in.getenv(EnvProject)), EnvProject},
		source{bindingValue(bound, binding.Project), "the binding for " + binding.Directory},
	)
	if project == "" {
		return "", "", &UsageError{fmt.Sprintf(
			"no project: pass --project <name>, bind %s with `vaultaffe setup`, or set %s.",
			in.Dir, EnvProject)}
	}
	return project, from, nil
}
