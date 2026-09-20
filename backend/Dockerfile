# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY LifeOS.Api/LifeOS.Api.csproj LifeOS.Api/
RUN dotnet restore LifeOS.Api/LifeOS.Api.csproj

COPY LifeOS.Api/ LifeOS.Api/

WORKDIR /src/LifeOS.Api
RUN dotnet publish LifeOS.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "LifeOS.Api.dll"]