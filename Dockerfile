# syntax=docker/dockerfile:1

FROM node:24-alpine AS styles
WORKDIR /source

COPY package.json package-lock.json ./
RUN npm ci

COPY src/SpaceForum.Web/Styles/ src/SpaceForum.Web/Styles/
COPY src/SpaceForum.Web/Components/ src/SpaceForum.Web/Components/
COPY src/SpaceForum.Web/Scripts/ src/SpaceForum.Web/Scripts/
RUN mkdir -p src/SpaceForum.Web/wwwroot/js && npm run assets:build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props ./
COPY src/SpaceForum.Domain/SpaceForum.Domain.csproj src/SpaceForum.Domain/
COPY src/SpaceForum.Application/SpaceForum.Application.csproj src/SpaceForum.Application/
COPY src/SpaceForum.Infrastructure/SpaceForum.Infrastructure.csproj src/SpaceForum.Infrastructure/
COPY src/SpaceForum.Web/SpaceForum.Web.csproj src/SpaceForum.Web/
RUN dotnet restore src/SpaceForum.Web/SpaceForum.Web.csproj

COPY src/ src/
COPY --from=styles /source/src/SpaceForum.Web/wwwroot/app.css src/SpaceForum.Web/wwwroot/app.css
COPY --from=styles /source/src/SpaceForum.Web/wwwroot/js/ src/SpaceForum.Web/wwwroot/js/
RUN dotnet publish src/SpaceForum.Web/SpaceForum.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "SpaceForum.Web.dll"]
