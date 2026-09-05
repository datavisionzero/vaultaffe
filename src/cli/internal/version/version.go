// Package version answers what release this binary is, and nothing else.
package version

// Value is the release this binary was cut from. The release build passes it
// with `-ldflags "-X …/internal/version.Value=<tag>"`, exactly as the .NET half
// takes `-p:Version=` from the same tag (Directory.Build.props). A build nobody
// tagged keeps the default and says so rather than claiming a number that was
// never released.
var Value = "0.0.0-dev"

// Released reports whether this binary was cut from a tag. The version
// handshake between CLI and server has to tell an installed release from a
// working copy, and this is the one place that knows.
func Released() bool { return Value != "0.0.0-dev" }
