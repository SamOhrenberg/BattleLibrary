# syntax=docker/dockerfile:1
# Self-hosted image: the API serves the built UI from a single origin.
# Build stages run on the builder's platform; only the runtime stage is per-arch.

FROM --platform=$BUILDPLATFORM node:20-slim AS frontend
WORKDIR /src
COPY FrontEnd/package.json FrontEnd/package-lock.json ./
RUN npm ci
COPY FrontEnd/ ./
# Same-origin API.
ENV VITE_API_BASE_URL=
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG TARGETARCH
WORKDIR /src
COPY BackEnd/Directory.Build.props ./
COPY BackEnd/src/BattleLibrary.Domain/BattleLibrary.Domain.csproj src/BattleLibrary.Domain/
COPY BackEnd/src/BattleLibrary.Infrastructure/BattleLibrary.Infrastructure.csproj src/BattleLibrary.Infrastructure/
COPY BackEnd/src/BattleLibrary.Api/BattleLibrary.Api.csproj src/BattleLibrary.Api/
RUN dotnet restore src/BattleLibrary.Api/BattleLibrary.Api.csproj -a $TARGETARCH
COPY BackEnd/src/ src/
RUN dotnet publish src/BattleLibrary.Api/BattleLibrary.Api.csproj -c Release -a $TARGETARCH \
    --no-restore --self-contained false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /src/dist ./wwwroot
# Clears the hosted site's consent URL so OAuth falls back to this origin.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    OAuth__FrontendUrl=""
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BattleLibrary.Api.dll"]
