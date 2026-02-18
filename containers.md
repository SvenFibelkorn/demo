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
