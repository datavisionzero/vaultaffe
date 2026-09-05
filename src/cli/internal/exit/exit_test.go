package exit

import (
	"testing"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/problem"
)

// The code comes from the problem before it comes from the status, because two
// of the codes this CLI has share a status with something else: human-only is a
// 403 that no wider token fixes, and unsupported-api-version is a 404 that is
// not a missing object.
func TestTheCodeSaysWhatKindOfOutcomeItWas(t *testing.T) {
	cases := []struct {
		name   string
		status int
		body   string
		want   int
	}{
		{"success", 200, "", OK},
		{"a missing object", 404, `{"type":"/problems/not-found","code":"not-found"}`, NotFound},
		{"a wrong field", 400, `{"type":"/problems/validation","code":"validation"}`, Refused},
		{"an existing name", 409, `{"type":"/problems/name-taken","code":"name-taken"}`, Conflict},
		{"past the window", 410, `{"type":"/problems/not-recoverable","code":"not-recoverable"}`, Gone},
		{"no token", 401, `{"type":"/problems/unauthenticated","code":"unauthenticated"}`, Denied},
		{"a missing scope", 403, `{"type":"/problems/insufficient-scope","code":"insufficient-scope"}`, Denied},
		{"bound elsewhere", 403, `{"type":"/problems/out-of-reach","code":"out-of-reach"}`, Denied},
		{"reserved for a person", 403, `{"type":"/problems/human-only","code":"human-only","humanAction":"purge"}`, HumanOnly},
		{"a client too old", 426, `{"type":"/problems/client-too-old","code":"client-too-old"}`, Skew},
		{"a contract this instance does not serve", 404, `{"type":"/problems/unsupported-api-version","code":"unsupported-api-version"}`, Skew},
		{"something that went wrong there", 500, `{"type":"/problems/internal","code":"internal"}`, Unexpected},
	}

	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if got := FromResponse(c.status, problem.Parse([]byte(c.body))); got != c.want {
				t.Fatalf("%d %s gave exit %d, want %d", c.status, c.body, got, c.want)
			}
		})
	}
}
