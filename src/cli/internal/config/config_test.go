package config

import (
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// The table is a prefix table and the longest entry wins: a binding on a
// monorepo root holds in every package under it, and one on a package wins
// inside that package. That is the whole of what "we search the tree upwards"
// buys a monorepo (Specification §6.2).
func TestTheLongestBoundDirectoryWins(t *testing.T) {
	file := File{Bindings: []Binding{
		{Directory: "/srv/monorepo", Project: "platform", Environment: "dev"},
		{Directory: "/srv/monorepo/services/billing", Project: "billing", Environment: "prod"},
	}}

	cases := []struct {
		dir     string
		project string
		bound   bool
	}{
		{"/srv/monorepo", "platform", true},
		{"/srv/monorepo/web", "platform", true},
		{"/srv/monorepo/services/billing", "billing", true},
		{"/srv/monorepo/services/billing/internal", "billing", true},
		{"/srv/elsewhere", "", false},
	}

	for _, c := range cases {
		binding, bound := file.BindingFor(c.dir)
		if bound != c.bound {
			t.Fatalf("%s: bound=%v, want %v", c.dir, bound, c.bound)
		}
		if bound && binding.Project != c.project {
			t.Fatalf("%s: project %q, want %q", c.dir, binding.Project, c.project)
		}
	}
}

// A prefix that is not a directory boundary is not a prefix: /srv/ap does not
// cover /srv/apple, and a `run` in the wrong project is not a typo one notices.
func TestAPrefixEndsAtADirectoryBoundary(t *testing.T) {
	file := File{Bindings: []Binding{{Directory: "/srv/ap", Project: "ap", Environment: "dev"}}}

	if _, bound := file.BindingFor("/srv/apple"); bound {
		t.Fatal("/srv/ap was taken to cover /srv/apple")
	}
}

func TestBindingTheSameDirectoryTwiceReplacesIt(t *testing.T) {
	var file File
	file.Bind(Binding{Directory: "/srv/app", Project: "app", Environment: "dev"})
	file.Bind(Binding{Directory: "/srv/app", Project: "app", Environment: "prod"})

	if len(file.Bindings) != 1 {
		t.Fatalf("the table holds %d entries, want 1", len(file.Bindings))
	}
	if file.Bindings[0].Environment != "prod" {
		t.Fatalf("the entry is %q, want prod", file.Bindings[0].Environment)
	}
}

// The file is found from here upwards, the way git finds its own directory, so
// that a monorepo needs one at its root rather than one per package.
func TestTheProjectFileIsFoundUpwards(t *testing.T) {
	root := t.TempDir()
	deep := filepath.Join(root, "services", "billing", "internal")
	if err := os.MkdirAll(deep, 0o755); err != nil {
		t.Fatal(err)
	}
	write(t, filepath.Join(root, ProjectFileName), "# the repository this is\nproject = billing\nenvironment = dev\n")

	found, ok, err := FindProjectFile(deep)
	if err != nil || !ok {
		t.Fatalf("no file found: %v %v", ok, err)
	}
	if found.Project != "billing" || found.Environment != "dev" {
		t.Fatalf("read %q/%q, want billing/dev", found.Project, found.Environment)
	}
}

// A key the file does not know is a mistake rather than something ignored: a
// misspelt `enviroment` that silently did nothing would point a `run` at the
// wrong environment, and that is the one thing this file must not be able to do
// quietly.
func TestAnUnknownKeyIsAMistake(t *testing.T) {
	dir := t.TempDir()
	write(t, filepath.Join(dir, ProjectFileName), "project = billing\nenviroment = dev\n")

	_, _, err := FindProjectFile(dir)
	var usage *UsageError
	if !errors.As(err, &usage) || !strings.Contains(usage.Message, "enviroment") {
		t.Fatalf("the misspelt key was not named: %v", err)
	}
}

// The file is checked in. A value in it would be a secret in the repository,
// which is the thing this product exists to end — so the word is refused by
// name rather than ignored as unknown.
func TestAValueInTheProjectFileIsRefusedByName(t *testing.T) {
	dir := t.TempDir()
	write(t, filepath.Join(dir, ProjectFileName), "project = billing\nenvironment = dev\nsecret = hunter2\n")

	_, _, err := FindProjectFile(dir)
	var usage *UsageError
	if !errors.As(err, &usage) || !strings.Contains(usage.Message, "holds no value") {
		t.Fatalf("a value in the project file was not refused: %v", err)
	}
}

// A token over plain HTTP is a token in somebody's network log. Loopback is the
// one place that cannot be true, which is what makes `localhost` work out of the
// box and nothing else quietly (Specification §6.3).
func TestPlainHTTPIsRefusedOffLoopback(t *testing.T) {
	cases := []struct {
		address  string
		override bool
		refused  bool
	}{
		{"https://vault.example.com", false, false},
		{"http://localhost:5142", false, false},
		{"http://127.0.0.1:5142", false, false},
		{"http://[::1]:5142", false, false},
		{"http://vault.example.com", false, true},
		{"http://vault.example.com", true, false},
		{"ftp://vault.example.com", false, true},
		{"vault.example.com", false, true},
	}

	for _, c := range cases {
		err := CheckAddress(c.address, c.override)
		if (err != nil) != c.refused {
			t.Fatalf("%s (override %v): %v", c.address, c.override, err)
		}
	}
}

// The environment wins, because that is how an agent receives its own token and
// how CI holds one — and it is the variable `run` strips from every child.
func TestTheEnvironmentWinsOverTheKeychain(t *testing.T) {
	in := Input{
		Getenv:       func(name string) string { return map[string]string{EnvToken: "vaultaffe_agent_x"}[name] },
		ReadKeychain: func(string) (string, error) { return "vaultaffe_session_y", nil },
	}

	token, from, err := in.ResolveToken("https://vault.example.com")
	if err != nil || token != "vaultaffe_agent_x" || from != EnvToken {
		t.Fatalf("token %q from %q: %v", token, from, err)
	}
}

// A file mode is the only protection a token in a file has. Shrugging at 0644
// would be the quiet fallback to plaintext this CLI refuses to make.
func TestATokenFileOthersCanReadIsRefused(t *testing.T) {
	path := filepath.Join(t.TempDir(), "token")
	write(t, path, "vaultaffe_session_x\n")
	if err := os.Chmod(path, 0o644); err != nil {
		t.Fatal(err)
	}

	_, err := ReadTokenFile(path)
	var usage *UsageError
	if !errors.As(err, &usage) || !strings.Contains(usage.Message, "chmod 600") {
		t.Fatalf("a world-readable token file was accepted: %v", err)
	}
}

func TestNoTokenNamesTheCommandThatGetsOne(t *testing.T) {
	in := Input{Getenv: func(string) string { return "" }}

	_, _, err := in.ResolveToken("https://vault.example.com")
	var usage *UsageError
	if !errors.As(err, &usage) || !strings.Contains(usage.Message, "vaultaffe login") {
		t.Fatalf("the way to a token was not named: %v", err)
	}
}

// Each half resolves through the same ladder and does so on its own: a
// --project on the command line does not throw away the environment this
// directory is bound to.
func TestEachHalfOfTheBindingResolvesOnItsOwn(t *testing.T) {
	in := Input{
		Getenv:  func(string) string { return "" },
		Dir:     "/srv/app",
		File:    File{Bindings: []Binding{{Directory: "/srv/app", Project: "app", Environment: "dev"}}},
		Project: "other",
	}

	project, environment, projectFrom, envFrom, err := in.ResolveBinding()
	if err != nil {
		t.Fatal(err)
	}
	if project != "other" || projectFrom != "--project" {
		t.Fatalf("project %q from %q", project, projectFrom)
	}
	if environment != "dev" || !strings.Contains(envFrom, "/srv/app") {
		t.Fatalf("environment %q from %q", environment, envFrom)
	}
}

// In CI and in containers the two variables are the binding, and there is no
// table at all (Specification §6.2).
func TestTheEnvironmentBindsWhereThereIsNoTable(t *testing.T) {
	in := Input{
		Getenv: func(name string) string {
			return map[string]string{EnvProject: "billing", EnvEnvironment: "prod"}[name]
		},
		Dir: "/build/workspace",
	}

	project, environment, _, _, err := in.ResolveBinding()
	if err != nil || project != "billing" || environment != "prod" {
		t.Fatalf("%q/%q: %v", project, environment, err)
	}
}

// An unbound directory is told what to do about it, and told it in the words of
// a command this CLI has.
func TestAnUnboundDirectoryNamesSetup(t *testing.T) {
	in := Input{Getenv: func(string) string { return "" }, Dir: "/srv/elsewhere"}

	_, _, _, _, err := in.ResolveBinding()
	var usage *UsageError
	if !errors.As(err, &usage) || !strings.Contains(usage.Message, "vaultaffe setup") {
		t.Fatalf("the way to a binding was not named: %v", err)
	}
}

// The configuration says which instances a person works against, and on a
// shared machine that is nobody else's business.
func TestTheConfigurationIsReadableByItsOwnerOnly(t *testing.T) {
	path := filepath.Join(t.TempDir(), "nested", "config.json")
	if err := Save(path, File{Instance: "https://vault.example.com"}); err != nil {
		t.Fatal(err)
	}

	info, err := os.Stat(path)
	if err != nil {
		t.Fatal(err)
	}
	if mode := info.Mode().Perm(); mode&0o077 != 0 {
		t.Fatalf("the configuration is mode %04o", mode)
	}

	read, err := Load(path)
	if err != nil || read.Instance != "https://vault.example.com" {
		t.Fatalf("read back %+v: %v", read, err)
	}
}

// A machine that has never logged in is not a machine with a broken
// installation.
func TestAMissingConfigurationIsAnEmptyOne(t *testing.T) {
	file, err := Load(filepath.Join(t.TempDir(), "nothing.json"))
	if err != nil {
		t.Fatal(err)
	}
	if file.Instance != "" || len(file.Bindings) != 0 {
		t.Fatalf("read %+v out of nothing", file)
	}
}

func write(t *testing.T, path, content string) {
	t.Helper()
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatal(err)
	}
}
