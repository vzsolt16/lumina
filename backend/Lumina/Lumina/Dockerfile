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
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Lumina.dll"]