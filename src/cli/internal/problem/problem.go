// Package problem reads the one error document this API has: RFC 9457, with a
// `code` that is also the last segment of `type` (docs/api.md, Refusals).
package problem

import (
	"encoding/json"
	"strings"
)

// Problem is application/problem+json as this CLI reads it. Extension members
// stay in Extra, so that a command can print what its own code carries — the
// action on `human-only`, the scopes on `insufficient-scope` — without this
// package having to know every code there will ever be.
type Problem struct {
	Type     string
	Title    string
	Status   int
	Detail   string
	Instance string
	Extra    map[string]json.RawMessage

	code string
}

// Parse reads a body as a problem document. A body that is not one — empty, or
// not JSON, or JSON without a `type` — is nil, and the caller falls back to the
// status alone.
func Parse(body []byte) *Problem {
	if len(body) == 0 {
		return nil
	}

	var raw map[string]json.RawMessage
	if err := json.Unmarshal(body, &raw); err != nil {
		return nil
	}
	if _, hasType := raw["type"]; !hasType {
		return nil
	}

	p := &Problem{Extra: map[string]json.RawMessage{}}
	for key, value := range raw {
		switch key {
		case "type":
			_ = json.Unmarshal(value, &p.Type)
		case "title":
			_ = json.Unmarshal(value, &p.Title)
		case "status":
			_ = json.Unmarshal(value, &p.Status)
		case "detail":
			_ = json.Unmarshal(value, &p.Detail)
		case "instance":
			_ = json.Unmarshal(value, &p.Instance)
		case "code":
			_ = json.Unmarshal(value, &p.code)
		default:
			p.Extra[key] = value
		}
	}

	if p.code == "" {
		p.code = lastSegment(p.Type)
	}

	return p
}

// Code is the refusal this document is, and the only thing a client switches
// on (docs/api.md). It is the member of that name, and the last segment of
// `type` for an instance old enough not to repeat it.
func (p *Problem) Code() string {
	if p == nil {
		return ""
	}
	return p.code
}

// Message is what goes to stderr: the detail when there is one, the title
// otherwise, and the code in brackets so that a person can look it up and a
// reader of a transcript can tell which refusal this was.
func (p *Problem) Message() string {
	if p == nil {
		return ""
	}
	text := p.Detail
	if text == "" {
		text = p.Title
	}
	if code := p.code; code != "" {
		if text == "" {
			return code
		}
		return text + " (" + code + ")"
	}
	return text
}

// String reads an extension member as a string: `humanAction` on `human-only`,
// `secretName` on `replace-required`. An absent or non-string member is "".
func (p *Problem) String(member string) string {
	if p == nil {
		return ""
	}
	raw, ok := p.Extra[member]
	if !ok {
		return ""
	}
	var value string
	if err := json.Unmarshal(raw, &value); err != nil {
		return ""
	}
	return value
}

// Strings reads an extension member as a list of strings: `requiredScopes` and
// `grantedScopes` on `insufficient-scope`, `apiVersions` on
// `unsupported-api-version`.
func (p *Problem) Strings(member string) []string {
	if p == nil {
		return nil
	}
	raw, ok := p.Extra[member]
	if !ok {
		return nil
	}
	var value []string
	if err := json.Unmarshal(raw, &value); err != nil {
		return nil
	}
	return value
}

func lastSegment(t string) string {
	if i := strings.LastIndexByte(t, '/'); i >= 0 {
		return t[i+1:]
	}
	return t
}
