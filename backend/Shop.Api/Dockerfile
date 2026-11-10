FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ShopMind.slnx ./
COPY backend/Shop.Api/Shop.Api.csproj backend/Shop.Api/
COPY backend/Shop.Application/Shop.Application.csproj backend/Shop.Application/
COPY backend/Shop.Domain/Shop.Domain.csproj backend/Shop.Domain/
COPY backend/Shop.Infrastructure/Shop.Infrastructure.csproj backend/Shop.Infrastructure/
RUN dotnet restore backend/Shop.Api/Shop.Api.csproj
COPY . .
WORKDIR /src/backend/Shop.Api
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
USER root
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 libkrb5-3 && rm -rf /var/lib/apt/lists/*
USER app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Shop.Api.dll"]
