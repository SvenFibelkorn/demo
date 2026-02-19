# Container images for `dotnet` and `scheduler`

The Dockerfiles are designed to be built from the repository root so they can include shared resources (`models/`, `corpus/`).

## Build images

```sh
docker build -f dotnet/Dockerfile -t demo-dotnet:latest .
docker build -f scheduler/Dockerfile -t demo-scheduler:latest .
```

## Run `dotnet` API container

```sh
docker run --rm -p 5271:8080 \
  -e ConnectionStrings__PostgresConnection="Host=host.docker.internal;Port=5432;Database=...;Username=...;Password=..." \
  -e Groq__ApiKey="..." \
  demo-dotnet:latest
```

## Run `scheduler` container

```sh
docker run --rm \
  -e ConnectionStrings__PostgresConnection="Host=host.docker.internal;Port=5432;Database=...;Username=...;Password=..." \
  -e ConnectionStrings__Redis="host.docker.internal:6379" \
  demo-scheduler:latest
```

## Kubernetes readiness notes

- Set configuration through environment variables (same keys as above) or mounted config.
- `dotnet` container listens on port `8080` (`ASPNETCORE_URLS=http://+:8080`).
- `scheduler` is a worker process (no public HTTP port required).
- Both images include `/models`; scheduler also includes `/corpus` for feed list files.

## Hetzner deployment (TLS + hardened exposure)

Use the production override and Caddy TLS termination:

```sh
cp .env.hetzner.example .env.hetzner
# edit .env.hetzner: DOMAIN, ACME_EMAIL, GRAFANA_ADMIN_PASSWORD, GROQ_API_KEY

docker compose --env-file .env.hetzner -f docker-compose.yml -f docker-compose.hetzner.yml up -d --build
```

### What changes in the Hetzner override

- Public traffic is served through Caddy on `80/443` with automatic Let's Encrypt certificates.
- `webapp` is exposed via HTTPS and `dotnet` is reachable through `/api` on the same domain.
- `postgres`, `redis`, and `grafana` are bound to `127.0.0.1` only.
- `alloy` OTEL ports are not published publicly.
- Grafana anonymous admin is disabled.

### ONNX model behavior in this repo

- The ONNX model is copied into images at build time from `models/`.
- The embedding services load local files from `../models/bge-base-en-v1.5/onnx/model.onnx` and `vocab.txt`.
- If files are missing, startup throws `FileNotFoundException`; there is no runtime model download in app code.

### Security notes for model handling

- Treat model files as supply-chain artifacts: pin source/version and verify checksums before putting them in `models/`.
- Keep model directory read-only in production and avoid downloading model artifacts at runtime.
- Keep only `80/443` open in the host firewall unless you intentionally need additional remote access.
