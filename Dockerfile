# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TravelCompanion.sln ./
COPY src/TravelCompanion.Api/TravelCompanion.Api.csproj src/TravelCompanion.Api/
COPY src/TravelCompanion.Shared/TravelCompanion.Shared.csproj src/TravelCompanion.Shared/
RUN dotnet restore src/TravelCompanion.Api/TravelCompanion.Api.csproj

COPY . .
RUN dotnet publish src/TravelCompanion.Api/TravelCompanion.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet TravelCompanion.Api.dll"]
