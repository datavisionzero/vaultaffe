package cmd

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/keychain"
)

// instance is a stand-in for a running installation: the routes a test needs,
// and a record of what reached them.
type instance struct {
	*httptest.Server
	t        *testing.T
	routes   map[string]http.HandlerFunc
	Requests []request
}

type request struct {
	Method string
	Path   string
	Query  string
	Header http.Header
	Body   []byte
}

func startInstanceStub(t *testing.T) *instance {
	t.Helper()

	in := &instance{t: t, routes: map[string]http.HandlerFunc{}}
	in.Server = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body := new(bytes.Buffer)
		_, _ = body.ReadFrom(r.Body)
		in.Requests = append(in.Requests, request{Method: r.Method, Path: r.URL.Path, Query: r.URL.RawQuery, Header: r.Header.Clone(), Body: body.Bytes()})

		// Every answer carries the release, refusals included (docs/api.md).
		w.Header().Set("Vaultaffe-Version", "0.1.0")
		if handler, ok := in.routes[r.Method+" "+r.URL.Path]; ok {
			handler(w, r)
			return
		}
		refuse(w, http.StatusNotFound, "not-found", "Nothing by that name.")
	}))
	t.Cleanup(in.Close)

	in.answer("GET /api/handshake", http.StatusOK, map[string]any{
		"product": "vaultaffe", "release": "0.1.0", "apiVersions": []string{"v1"}, "minimumClient": "0.1.0",
	})
	return in
}

func (in *instance) route(key string, handler http.HandlerFunc) { in.routes[key] = handler }

func (in *instance) answer(key string, status int, body any) {
	in.route(key, func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(status)
		_ = json.NewEncoder(w).Encode(body)
	})
}

func (in *instance) refuse(key string, status int, code, detail string, extra ...map[string]any) {
	in.route(key, func(w http.ResponseWriter, _ *http.Request) {
		refuse(w, status, code, detail, extra...)
	})
}

func (in *instance) sent(method, path string) *request {
	in.t.Helper()
	for i := range in.Requests {
		if in.Requests[i].Method == method && in.Requests[i].Path == path {
			return &in.Requests[i]
		}
	}
	in.t.Fatalf("no %s %s reached the instance; it saw %v", method, path, in.Requests)
	return nil
}

func refuse(w http.ResponseWriter, status int, code, detail string, extra ...map[string]any) {
	document := map[string]any{
		"type": "/problems/" + code, "title": detail, "status": status, "detail": detail, "code": code,
	}
	for _, more := range extra {
		for key, value := range more {
			document[key] = value
		}
	}
	w.Header().Set("Content-Type", "application/problem+json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(document)
}

// console is one invocation: the environment it ran in and what it wrote.
type console struct {
	Code     int
	Stdout   string
	Stderr   string
	Keychain map[string]string
	Config   config.File
	Dir      string
	// What `run` would have replaced this process with, and with what
	// environment (ADR 0009). Nil where exec() was never reached.
	Exec *replacement
}

type replacement struct {
	Path        string
	Argv        []string
	Environment []string
}

// Value answers what the child would have found in name, and whether it is
// there at all.
func (r *replacement) Value(name string) (string, bool) {
	for _, entry := range r.Environment {
		if key, value, found := strings.Cut(entry, "="); found && key == name {
			return value, true
		}
	}
	return "", false
}

// Everything says nothing leaked: the two streams and the files this CLI wrote.
func (c console) mentions(text string) bool {
	return strings.Contains(c.Stdout, text) || strings.Contains(c.Stderr, text)
}

type session struct {
	dir      string
	env      map[string]string
	keychain map[string]string
	stdin    string
	environ  []string
	store    func(instance, token string) error
}

func newSession(t *testing.T, in *instance) *session {
	t.Helper()

	dir := t.TempDir()
	return &session{
		dir:      dir,
		keychain: map[string]string{},
		env: map[string]string{
			config.EnvURL:      in.URL,
			"VAULTAFFE_CONFIG": filepath.Join(dir, "config", "config.json"),
			"HOME":             dir,
		},
	}
}

func (s *session) run(t *testing.T, args ...string) console {
	t.Helper()

	stdout, stderr := new(bytes.Buffer), new(bytes.Buffer)
	store := s.store
	if store == nil {
		store = func(instance, token string) error { s.keychain[instance] = token; return nil }
	}

	var replaced *replacement

	code := Run(context.Background(), args, Env{
		Getenv:  func(name string) string { return s.env[name] },
		Dir:     s.dir,
		Stdin:   strings.NewReader(s.stdin),
		Stdout:  stdout,
		Stderr:  stderr,
		Environ: func() []string { return s.environ },
		Exec: func(path string, argv []string, environment []string) error {
			replaced = &replacement{Path: path, Argv: argv, Environment: environment}
			return nil
		},
		Keychain: Keychain{
			Read: func(instance string) (string, error) {
				token, ok := s.keychain[instance]
				if !ok {
					return "", keychain.ErrNotFound
				}
				return token, nil
			},
			Store:  store,
			Forget: func(instance string) error { delete(s.keychain, instance); return nil },
		},
	})

	file, err := config.Load(s.env["VAULTAFFE_CONFIG"])
	if err != nil {
		t.Fatal(err)
	}

	return console{
		Code:     code,
		Stdout:   stdout.String(),
		Stderr:   stderr.String(),
		Keychain: s.keychain,
		Config:   file,
		Dir:      s.dir,
		Exec:     replaced,
	}
}
