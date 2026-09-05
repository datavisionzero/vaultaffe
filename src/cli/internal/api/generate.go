// Package api is the Go client of `docs/api/openapi.json`, generated from it
// (ADR 0011). The generated file is not committed: `go generate ./...` produces
// it before vet, test and build, locally and in CI alike, so that a working
// tree is never a state where the client agrees with a contract that has moved.
package api

//go:generate go tool oapi-codegen -config oapi-codegen.yaml ../../../../docs/api/openapi.json
