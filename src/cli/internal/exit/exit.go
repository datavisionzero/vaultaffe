// Package exit is the table a script branches on: one code per kind of
// outcome, derived from the status and the problem code so that nothing has to
// parse a sentence (docs/cli.md).
package exit

import "github.com/datavisionzero/vaultaffe/src/cli/internal/problem"

const (
	// OK is success: 2xx.
	OK = 0
	// Unexpected is a 500, an answer this CLI cannot parse, or a bug in it.
	Unexpected = 1
	// Usage is bad arguments, no instance, no token, or no binding for this directory.
	Usage = 2
	// NotFound is 404: nothing by that name.
	NotFound = 3
	// Refused is 400 validation.
	Refused = 4
	// Conflict is 409: name-taken, replace-required, already-started.
	Conflict = 5
	// Gone is 410 not-recoverable: deleted longer ago than the window.
	Gone = 6
	// Denied is 401 and 403 — unauthenticated, forbidden, insufficient-scope, out-of-reach.
	Denied = 7
	// HumanOnly is the short list of Specification §6.4 that no token can be
	// given. It is its own code because it is the one refusal an agent acts on
	// differently: it does not retry with a wider token, it asks a person.
	HumanOnly = 8
	// Skew is a CLI too old or too new for the instance (docs/api.md).
	Skew = 9
	// Unreachable is DNS, a refused connection, a timeout, TLS: nothing answered.
	Unreachable = 10
	// Placeholder is `run` stopping on an empty placeholder: a human still has
	// to do something, and it is not the same outcome as a failure of the API.
	Placeholder = 11
)

// FromResponse derives the code from a status and the problem document that
// came with it, if any.
func FromResponse(status int, p *problem.Problem) int {
	switch {
	case status >= 200 && status < 300:
		return OK
	case p.Code() == "human-only":
		return HumanOnly
	case p.Code() == "client-too-old" || p.Code() == "client-version-unreadable" || p.Code() == "unsupported-api-version":
		return Skew
	case status == 401 || status == 403:
		return Denied
	case status == 404:
		return NotFound
	case status == 400:
		return Refused
	case status == 409:
		return Conflict
	case status == 410:
		return Gone
	default:
		return Unexpected
	}
}
