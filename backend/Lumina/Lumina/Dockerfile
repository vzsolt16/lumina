FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Lumina/Lumina.csproj Lumina/
RUN dotnet restore Lumina/Lumina.csproj

COPY Lumina/ Lumina/

RUN dotnet publish Lumina/Lumina.csproj \
    --configuration Release \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl gosu \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data \
    && chown -R app:app /app
COPY --chmod=755 docker-entrypoint.sh /usr/local/bin/lumina-entrypoint
USER root
EXPOSE 8080
ENTRYPOINT ["lumina-entrypoint"]
