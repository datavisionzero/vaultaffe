# One image, carrying the API and the built SPA. The web application is a client
# of the HTTP API exactly as the CLI is (Specification §9), but it is *served*
# from the same process — one `docker compose up` is the whole product, and the
# browser reaches the instance at its own origin
# (ADR 0016). The two toolchains meet here and nowhere else; the runtime image
# carries neither of them.
#
# Build context is the repository root:
#   docker build -f deploy/Dockerfile -t vaultaffe .

# ---------------------------------------------------------------- the SPA ----
FROM node:24-alpine AS web
WORKDIR /web

COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci

# The API layer is generated from the contract before the build runs (ADR 0006),
# so the document has to be in this stage — at the path `npm run generate`
# reaches for, two levels above src/web/. Copied before the sources because it
# changes less often than they do.
COPY docs/api/openapi.json /docs/api/openapi.json

COPY src/web/ ./

# `--outDir` overrides the one in vite.config.ts, which points at the API's
# `wwwroot` so that a local build gives a developer the whole product from one
# `dotnet run`. Here the two are separate stages and the copy below is what
# joins them.
RUN npm run build -- --outDir dist --emptyOutDir

# ------------------------------------------------------------- the server ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# What this build calls itself. The release workflow passes the tag; a build
# started by hand keeps the default the props file gives, which says plainly
# that it is not a release (Directory.Build.props). The Go half of a release
# takes the same string through its linker flag, so the two cannot disagree
# about which release they are.
ARG VERSION=0.0.0-dev

# Which commit it was built from, appended by the SDK to the informational
# version behind a `+`. CI passes it, because this build has no repository to
# read it out of — `Dockerfile.dockerignore` excludes `.git/`. Left empty it
# stamps nothing, and `InstanceVersion` cuts the suffix off anyway: the commit
# is the build's business, the semver is what the CLI compares.
ARG REVISION=

# Restore against the manifests alone, so that a change to a source file does
# not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Vaultaffe.Domain/Vaultaffe.Domain.csproj src/Vaultaffe.Domain/
COPY src/Vaultaffe.Application/Vaultaffe.Application.csproj src/Vaultaffe.Application/
COPY src/Vaultaffe.Infrastructure/Vaultaffe.Infrastructure.csproj src/Vaultaffe.Infrastructure/
COPY src/Vaultaffe.Api/Vaultaffe.Api.csproj src/Vaultaffe.Api/
RUN dotnet restore src/Vaultaffe.Api/Vaultaffe.Api.csproj

COPY src/Vaultaffe.Domain/ src/Vaultaffe.Domain/
COPY src/Vaultaffe.Application/ src/Vaultaffe.Application/
COPY src/Vaultaffe.Infrastructure/ src/Vaultaffe.Infrastructure/
COPY src/Vaultaffe.Api/ src/Vaultaffe.Api/
RUN dotnet publish src/Vaultaffe.Api/Vaultaffe.Api.csproj -c Release -o /app \
    -p:Version=$VERSION -p:SourceRevisionId=$REVISION

# ------------------------------------------------------------ the runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Npgsql probes for GSSAPI when it opens a connection, whatever the connection
# string says about authentication. Without the library the probe writes two
# lines with the word `Error` straight to stderr on every start of a run that is
# going fine, and the next person reading the log during a real outage has to
# rule it out first. Three megabytes to keep the log honest; nothing here asks
# for Kerberos. Ahead of the COPY, so a source change does not re-run apk.
RUN apk add --no-cache krb5-libs

COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot

# Plain HTTP, and only on the compose network: Caddy in front of this is what
# holds a certificate (Specification §6.3). A container that terminated TLS
# itself would need the key material in here as well as in front of it.
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

# Reachable over the network, so it does not run as root. The one thing this
# process holds that nothing else does is the master key, and it holds it in
# memory: there is no path it writes and no volume it needs.
RUN chown -R $APP_UID:$APP_UID /app
USER $APP_UID

# The assembly keeps its own name rather than taking `vaultaffe`, which belongs
# to the Go binary a developer types (Vaultaffe.Api.csproj). Nobody types this
# one: it is the entry point of a container.
ENTRYPOINT ["./Vaultaffe.Api"]

# Where this image came from, said by the image itself. The package page on a
# registry carries the same link, but that is a property of the registry: an
# image somebody mirrors into their own keeps these and loses that. Two of them
# repeat the arguments the build stage consumed, so they are declared again
# here — an ARG belongs to one stage.
#
# Last, after the COPYs and after the only RUN that costs anything, because
# `revision` changes on every commit and a label is the cheapest layer there is.
# Nothing below it has to be rebuilt when it does.
ARG VERSION=0.0.0-dev
ARG REVISION=
LABEL org.opencontainers.image.title="vaultaffe" \
      org.opencontainers.image.description="A self-hosted secrets manager for small teams that work with CLI-based AI agents." \
      org.opencontainers.image.source="https://github.com/datavisionzero/vaultaffe" \
      org.opencontainers.image.documentation="https://github.com/datavisionzero/vaultaffe/blob/main/docs/codebase.md" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="datavisionzero" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION"
