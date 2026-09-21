FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY backend/LifeOS.Api/LifeOS.Api.csproj backend/LifeOS.Api/

RUN dotnet restore backend/LifeOS.Api/LifeOS.Api.csproj

COPY backend/ backend/

WORKDIR /src/backend/LifeOS.Api

RUN dotnet publish LifeOS.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

# Blitz menjalankan container sebagai user 1000
USER 1000:1000

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "LifeOS.Api.dll"]