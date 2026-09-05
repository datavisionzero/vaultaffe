package cmd

import (
	"fmt"
	"time"

	"github.com/google/uuid"
	openapi_types "github.com/oapi-codegen/runtime/types"

	"github.com/datavisionzero/vaultaffe/src/cli/internal/config"
)

// when prints a moment the way a person reads one, and a dash for what has not
// happened: a version that is still current has no replacement, and a key that
// is a placeholder has never been written.
func when(moment *time.Time) string {
	if moment == nil || moment.IsZero() {
		return "—"
	}
	return moment.Local().Format("2006-01-02 15:04")
}

// parseID reads an id off the command line. It is a usage error rather than a
// request the instance would refuse anyway: a mistyped id is a mistake at this
// end, and the round trip would add nothing to what we already know.
func parseID(value string) (openapi_types.UUID, error) {
	parsed, err := uuid.Parse(value)
	if err != nil {
		return openapi_types.UUID{}, &config.UsageError{Message: fmt.Sprintf("%q is not an id.", value)}
	}
	return parsed, nil
}
