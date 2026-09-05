// `vaultaffe`: a client of the public HTTP API and nothing else. It references
// nothing under src/ and knows an instance only through that API
// (Specification §9), which is what lets the same binary run on a laptop, a CI
// runner and inside an agent's container.
module github.com/datavisionzero/vaultaffe/src/cli

go 1.27.1
