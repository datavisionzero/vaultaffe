// Package client wraps the generated client with what every request carries and
// what every answer is checked for: the bearer token, the release this build is,
// and the refusal turned into a sentence and an exit code (docs/api.md).
package client

import (
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"net/url"
	"os"
	"runtime"
	"strings"
	"time"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/api"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/exit"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/problem"
	"github.com/datavisionzero/vaultaffe/src/cli/internal/version"
)

// ClientHeader is what this build calls itself on every request, and what the
// instance refuses a too-old one by (docs/api.md, The version exchange).
const ClientHeader = "Vaultaffe-Client"

// VersionHeader is the instance's release, on every answer including refusals.
const VersionHeader = "Vaultaffe-Version"

// Failure is an answer this CLI turns into an exit code: what to print to
// stderr, and which code to leave with.
type Failure struct {
	Code    int
	Message string
	Problem *problem.Problem
}

func (f *Failure) Error() string { return f.Message }

// Client is one invocation's client.
type Client struct {
	*api.ClientWithResponses
	Address string
}

// New builds a client for one address and one token. The token is a bearer
// header and nothing else: this CLI never writes it anywhere a request did not
// need it.
func New(address, token string, httpClient *http.Client) (*Client, error) {
	generated, err := api.NewClientWithResponses(
		address,
		api.WithHTTPClient(httpClient),
		api.WithRequestEditorFn(func(_ context.Context, req *http.Request) error {
			if token != "" {
				req.Header.Set("Authorization", "Bearer "+token)
			}
			req.Header.Set(ClientHeader, version.Value)
			req.Header.Set("User-Agent", UserAgent())
			return nil
		}))
	if err != nil {
		return nil, err
	}
	return &Client{ClientWithResponses: generated, Address: address}, nil
}

// UserAgent is `vaultaffe/<release> (<os>/<arch>)`.
func UserAgent() string {
	return fmt.Sprintf("vaultaffe/%s (%s/%s)", version.Value, runtime.GOOS, runtime.GOARCH)
}

// Default is the HTTP client every command but a device-login poll uses: a
// timeout, because nothing an agent runs may hang forever.
func Default() *http.Client { return &http.Client{Timeout: 30 * time.Second} }

// Check turns an answer into a Failure when it is one. Version skew is read
// first and separately: a client the instance will not serve has to arrive as a
// sentence rather than as a field that failed to parse
// (Specification §6.3).
func Check(resp *http.Response, body []byte) error {
	if resp == nil {
		return &Failure{Code: exit.Unexpected, Message: "nothing answered"}
	}
	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		return nil
	}

	p := problem.Parse(body)
	message := p.Message()
	if message == "" {
		message = fmt.Sprintf("the instance answered %s", strings.TrimSpace(resp.Status))
	}

	// The one refusal the instance cannot word for us: it says which contracts
	// it serves, and only this build knows which one it speaks.
	if p.Code() == "unsupported-api-version" {
		served := strings.Join(p.Strings("apiVersions"), ", ")
		if served == "" {
			served = "none it named"
		}
		message = fmt.Sprintf(
			"this CLI speaks the %s contract and %s serves %s: one of the two is behind the other.",
			version.Contract, resp.Request.URL.Host, served)
	}

	return &Failure{Code: exit.FromResponse(resp.StatusCode, p), Message: message, Problem: p}
}

// Transport turns an error from the HTTP client into the Failure it is: nothing
// answered, and that is its own exit code because it is the one failure that is
// about the network rather than about the request.
func Transport(err error) error {
	if err == nil {
		return nil
	}

	var failure *Failure
	if errors.As(err, &failure) {
		return failure
	}

	var urlErr *url.Error
	var netErr net.Error
	if errors.As(err, &urlErr) || errors.As(err, &netErr) ||
		errors.Is(err, context.DeadlineExceeded) || errors.Is(err, os.ErrDeadlineExceeded) {
		return &Failure{Code: exit.Unreachable, Message: fmt.Sprintf("the instance could not be reached: %v", err)}
	}

	return &Failure{Code: exit.Unexpected, Message: err.Error()}
}

// Handshake asks what this instance is and whether the two agree on anything,
// before a token is sent to it. It is a request of its own rather than a check
// on every answer: the instance refuses a client that is too old by itself, and
// paying a round trip per command to learn what a refusal would have said is a
// cost every `run` would carry.
func (c *Client) Handshake(ctx context.Context) (*api.Handshake, error) {
	resp, err := c.HandshakeWithResponse(ctx)
	if err != nil {
		return nil, Transport(err)
	}
	if err := Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	if resp.JSON200 == nil {
		return nil, &Failure{Code: exit.Unexpected, Message: "the instance answered a handshake this CLI cannot read"}
	}

	shake := resp.JSON200
	if shake.Product != "vaultaffe" {
		return nil, &Failure{Code: exit.Skew, Message: fmt.Sprintf(
			"%s is not a vaultaffe instance: it calls itself %q.", c.Address, shake.Product)}
	}
	if !slicesContains(shake.ApiVersions, version.Contract) {
		return nil, &Failure{Code: exit.Skew, Message: fmt.Sprintf(
			"this CLI speaks the %s contract and %s serves %s: one of the two is behind the other.",
			version.Contract, c.Address, strings.Join(shake.ApiVersions, ", "))}
	}
	return shake, nil
}

func slicesContains(values []string, wanted string) bool {
	for _, value := range values {
		if value == wanted {
			return true
		}
	}
	return false
}
