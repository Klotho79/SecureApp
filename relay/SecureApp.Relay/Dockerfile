# Build context is the REPO ROOT, not this folder — SecureApp.Relay.csproj references
# ..\..\src\SecureApp.Domain\SecureApp.Domain.csproj by relative path, so the Domain project
# must be present in the build context too. Build from the repo root with:
#   docker build -f relay/SecureApp.Relay/Dockerfile -t secureapp-relay .
#
# No --platform needed when building directly on the Raspberry Pi (arm64 host) — Docker
# resolves the matching arm64 variant of these multi-arch base images automatically.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/SecureApp.Domain/ src/SecureApp.Domain/
COPY relay/SecureApp.Relay/ relay/SecureApp.Relay/
WORKDIR /src/relay/SecureApp.Relay
RUN dotnet publish SecureApp.Relay.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SecureApp.Relay.dll"]
