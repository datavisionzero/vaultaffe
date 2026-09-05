// Package config is where the CLI learns three things: which instance it talks
// to, as whom, and which project and environment this directory means
// (Specification §6.2).
//
// The directory binding is a prefix table in the *user's* configuration and not
// a file in the repository — Doppler's most underrated idea. A checked-in
// project file is optional and does one thing: `vaultaffe setup` reads it and
// writes the entry. It is never a second source of truth consulted at run time,
// because a repository that could rebind somebody's directories by being cloned
// would be a repository that can point a `run` at production.
package config

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"net/url"
	"os"
	"path/filepath"
	"strings"
)

// ProjectFileName is the optional checked-in file: project and environment, and
// no secrets, ever.
const ProjectFileName = ".vaultaffe"

// UsageError is a mistake in the environment or the arguments: exit 2. Its
// message is a sentence a person can act on, and it never names a command this
// CLI does not have (ADR 0010).
type UsageError struct{ Message string }

func (e *UsageError) Error() string { return e.Message }

// Binding is one entry of the prefix table: everything at or below Directory
// means this project and this environment.
type Binding struct {
	Directory   string `json:"directory"`
	Project     string `json:"project"`
	Environment string `json:"environment"`
}

// File is the user's configuration, as it is on disk. It holds the instance
// this machine last logged in to, where the session token went when it did not
// go into the keychain, and the prefix table. It holds **no token** and no
// secret value: the token file, if there is one, is its own file with its own
// permissions.
type File struct {
	Instance  string    `json:"instance,omitempty"`
	TokenFile string    `json:"tokenFile,omitempty"`
	Bindings  []Binding `json:"bindings,omitempty"`
}

// Path is where the configuration lives: $VAULTAFFE_CONFIG if it is set,
// otherwise $XDG_CONFIG_HOME/vaultaffe/config.json, otherwise
// ~/.config/vaultaffe/config.json.
func Path(getenv func(string) string) (string, error) {
	if explicit := strings.TrimSpace(getenv("VAULTAFFE_CONFIG")); explicit != "" {
		return explicit, nil
	}
	if xdg := strings.TrimSpace(getenv("XDG_CONFIG_HOME")); xdg != "" {
		return filepath.Join(xdg, "vaultaffe", "config.json"), nil
	}
	home := strings.TrimSpace(getenv("HOME"))
	if home == "" {
		var err error
		if home, err = os.UserHomeDir(); err != nil {
			return "", &UsageError{"no home directory: set VAULTAFFE_CONFIG to say where the configuration lives."}
		}
	}
	return filepath.Join(home, ".config", "vaultaffe", "config.json"), nil
}

// Load reads the configuration. A file that is not there is an empty one: a
// machine that has never logged in is not a machine with a broken installation.
func Load(path string) (File, error) {
	content, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return File{}, nil
	}
	if err != nil {
		return File{}, &UsageError{fmt.Sprintf("%s could not be read: %v", path, err)}
	}

	var file File
	if err := json.Unmarshal(content, &file); err != nil {
		return File{}, &UsageError{fmt.Sprintf("%s is not readable as configuration: %v", path, err)}
	}
	return file, nil
}

// Save writes the configuration, readable by its owner and nobody else. The
// directory is created with the same intent: what is in it says which instances
// a person works against, which is nobody else's business on a shared machine.
func Save(path string, file File) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		return err
	}

	content, err := json.MarshalIndent(file, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(path, append(content, '\n'), 0o600)
}

// BindingFor answers the entry that covers dir: the longest directory that is a
// prefix of it, so that a binding set on a monorepo root holds in every package
// under it and one set on a package wins inside that package.
func (f File) BindingFor(dir string) (Binding, bool) {
	dir = clean(dir)

	var best Binding
	found := false
	for _, binding := range f.Bindings {
		candidate := clean(binding.Directory)
		if !covers(candidate, dir) {
			continue
		}
		if !found || len(candidate) > len(clean(best.Directory)) {
			best, found = binding, true
		}
	}
	return best, found
}

// Bind adds or replaces the entry for a directory. Setting one twice is the
// normal way of changing an environment and does not leave the old one behind.
func (f *File) Bind(binding Binding) {
	binding.Directory = clean(binding.Directory)
	for i, existing := range f.Bindings {
		if clean(existing.Directory) == binding.Directory {
			f.Bindings[i] = binding
			return
		}
	}
	f.Bindings = append(f.Bindings, binding)
}

// Unbind removes the entry for exactly this directory and says whether there
// was one.
func (f *File) Unbind(dir string) bool {
	dir = clean(dir)
	for i, existing := range f.Bindings {
		if clean(existing.Directory) == dir {
			f.Bindings = append(f.Bindings[:i], f.Bindings[i+1:]...)
			return true
		}
	}
	return false
}

func clean(dir string) string {
	if dir == "" {
		return dir
	}
	return filepath.Clean(dir)
}

// covers reports whether dir is at or below prefix, on path boundaries: /srv/ap
// does not cover /srv/apple.
func covers(prefix, dir string) bool {
	if prefix == "" {
		return false
	}
	if prefix == dir {
		return true
	}
	if !strings.HasSuffix(prefix, string(filepath.Separator)) {
		prefix += string(filepath.Separator)
	}
	return strings.HasPrefix(dir, prefix)
}

// ProjectFile is the optional checked-in file, once read.
type ProjectFile struct {
	Path        string
	Project     string
	Environment string
}

// FindProjectFile walks up from dir the way git finds its own directory, so
// that a monorepo needs one file at its root rather than one per package. No
// file is not an error — the file is optional.
func FindProjectFile(dir string) (ProjectFile, bool, error) {
	dir = clean(dir)
	for {
		candidate := filepath.Join(dir, ProjectFileName)
		if info, err := os.Stat(candidate); err == nil && !info.IsDir() {
			file, err := parseProjectFile(candidate)
			return file, err == nil, err
		}

		parent := filepath.Dir(dir)
		if parent == dir {
			return ProjectFile{}, false, nil
		}
		dir = parent
	}
}

// parseProjectFile reads `key = value` lines, `#` starts a comment. Two keys
// are known, and anything else is a mistake rather than something ignored: a
// misspelt `enviroment` that silently did nothing would point a `run` at the
// wrong environment, which is the one mistake this file must not be able to
// make quietly. `secret` is refused by name, because the answer to "can I put
// one here" has to be a sentence rather than a shrug.
func parseProjectFile(path string) (ProjectFile, error) {
	handle, err := os.Open(path)
	if err != nil {
		return ProjectFile{}, &UsageError{fmt.Sprintf("%s could not be read: %v", path, err)}
	}
	defer handle.Close()

	file := ProjectFile{Path: path}
	scanner := bufio.NewScanner(handle)
	line := 0
	for scanner.Scan() {
		line++
		text := strings.TrimSpace(scanner.Text())
		if text == "" || strings.HasPrefix(text, "#") {
			continue
		}

		key, value, ok := strings.Cut(text, "=")
		if !ok {
			return ProjectFile{}, &UsageError{fmt.Sprintf("%s line %d: expected `key = value`.", path, line)}
		}

		key, value = strings.TrimSpace(key), strings.TrimSpace(value)
		switch key {
		case "project":
			file.Project = value
		case "environment":
			file.Environment = value
		case "secret", "secrets", "value", "values":
			return ProjectFile{}, &UsageError{fmt.Sprintf("%s line %d: %q. This file names a project and an environment and holds no value; that is why it can be checked in.", path, line, key)}
		default:
			return ProjectFile{}, &UsageError{fmt.Sprintf("%s line %d: unknown key %q; the file knows `project` and `environment`.", path, line, key)}
		}
	}
	if err := scanner.Err(); err != nil {
		return ProjectFile{}, &UsageError{fmt.Sprintf("%s could not be read: %v", path, err)}
	}

	if file.Project == "" {
		return ProjectFile{}, &UsageError{fmt.Sprintf("%s has no `project = name` line.", path)}
	}
	if file.Environment == "" {
		return ProjectFile{}, &UsageError{fmt.Sprintf("%s has no `environment = name` line.", path)}
	}
	return file, nil
}

// CheckAddress refuses plain HTTP to anything but a loopback host
// (Specification §6.3). A token over plain HTTP is a token in somebody's network
// log, and `localhost` is the one place that cannot be true. Override is
// explicit and never inferred.
func CheckAddress(address string, allowPlainHTTP bool) error {
	parsed, err := url.Parse(address)
	if err != nil || !parsed.IsAbs() || parsed.Host == "" {
		return &UsageError{fmt.Sprintf("%q is not an address: scheme and host, like https://vault.example.com.", address)}
	}
	switch parsed.Scheme {
	case "https":
		return nil
	case "http":
		if allowPlainHTTP || IsLoopback(parsed.Hostname()) {
			return nil
		}
		return &UsageError{fmt.Sprintf(
			"%s is plain HTTP to a host that is not loopback, and a token over plain HTTP is a token in the network log. Use https://, or pass --insecure-http (or VAULTAFFE_INSECURE_HTTP=1) to say you mean it.",
			address)}
	default:
		return &UsageError{fmt.Sprintf("%q is neither http nor https.", address)}
	}
}

// IsLoopback is the whole of what "this machine" means here: the two names and
// the two address families, and nothing that merely resolves to one.
func IsLoopback(host string) bool {
	if host == "localhost" || strings.HasSuffix(host, ".localhost") {
		return true
	}
	if ip := net.ParseIP(strings.Trim(host, "[]")); ip != nil {
		return ip.IsLoopback()
	}
	return false
}
