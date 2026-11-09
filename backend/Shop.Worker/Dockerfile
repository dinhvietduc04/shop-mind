FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ShopMind.slnx ./
COPY backend/Shop.Worker/Shop.Worker.csproj backend/Shop.Worker/
COPY backend/Shop.Application/Shop.Application.csproj backend/Shop.Application/
COPY backend/Shop.Domain/Shop.Domain.csproj backend/Shop.Domain/
COPY backend/Shop.Infrastructure/Shop.Infrastructure.csproj backend/Shop.Infrastructure/
RUN dotnet restore backend/Shop.Worker/Shop.Worker.csproj
COPY . .
WORKDIR /src/backend/Shop.Worker
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
USER root
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 libkrb5-3 && rm -rf /var/lib/apt/lists/*
USER app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Shop.Worker.dll"]
