package version

import "testing"

// The default is load-bearing: it is what the handshake reads when nobody
// passed a tag, and a build that quietly called itself `1.0.0` would be worse
// than one that admits it is a working copy.
func TestAnUntaggedBuildSaysSo(t *testing.T) {
	if Value != "0.0.0-dev" {
		t.Fatalf("the default version is %q, want %q", Value, "0.0.0-dev")
	}

	if Released() {
		t.Fatal("an untagged build reports itself as released")
	}
}

func TestATaggedBuildIsReleased(t *testing.T) {
	original := Value
	t.Cleanup(func() { Value = original })

	Value = "1.2.3"

	if !Released() {
		t.Fatalf("version %q does not report itself as released", Value)
	}
}
