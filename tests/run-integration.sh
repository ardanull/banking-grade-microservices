#!/usr/bin/env bash
set -euo pipefail
docker compose up -d --build
trap "docker compose down -v" EXIT
dotnet test tests/Banking.Quality.Tests/Banking.Quality.Tests.csproj -c Release
