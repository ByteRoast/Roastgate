# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so that this layer is cached until a project file changes.
COPY Directory.Build.props .editorconfig ./
COPY src/Roastgate.Core/Roastgate.Core.csproj src/Roastgate.Core/
COPY src/Roastgate.Web/Roastgate.Web.csproj src/Roastgate.Web/
RUN dotnet restore src/Roastgate.Web/Roastgate.Web.csproj

COPY src/ src/

# No --no-restore on purpose: the layer above restored without wwwroot present, and publishing on top of that
# silently drops the Blazor framework scripts (_framework/blazor.web.js), which leaves a page that never becomes interactive.
RUN dotnet publish src/Roastgate.Web/Roastgate.Web.csproj -c Release -o /out \
    && test -f /out/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# nsenter (util-linux) runs nginx and certbot in the host namespaces.
RUN apt-get update \
    && apt-get install -y --no-install-recommends util-linux \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /out .

# `docker exec -it roastgate roastgate reset-password`
RUN printf '#!/bin/sh\nexec /app/Roastgate.Web "$@"\n' > /usr/local/bin/roastgate \
    && chmod +x /usr/local/bin/roastgate

ENV ASPNETCORE_HTTP_PORTS=8181 \
    ROASTGATE_DATA_DIR=/data \
    ROASTGATE_HOST_EXEC=nsenter

# Roastgate needs root: nsenter into the host namespaces requires it.
VOLUME /data
EXPOSE 8181
ENTRYPOINT ["/app/Roastgate.Web"]
