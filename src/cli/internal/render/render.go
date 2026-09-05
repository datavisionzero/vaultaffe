// Package render is how this CLI prints. Two rules hold everywhere in it: data
// goes to stdout and a sentence for a person goes to stderr, and a value is
// printed only where the command was explicitly asked for one
// (Specification §4).
package render

import (
	"encoding/json"
	"io"
)

// JSON prints v the way the API answered it, indented, with HTML escaping off
// so that a `&` in a name stays a `&`.
func JSON(w io.Writer, v any) error {
	encoder := json.NewEncoder(w)
	encoder.SetIndent("", "  ")
	encoder.SetEscapeHTML(false)
	return encoder.Encode(v)
}
