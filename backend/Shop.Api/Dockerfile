FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ShopMind.slnx ./
COPY src/Shop.Api/Shop.Api.csproj src/Shop.Api/
COPY src/Shop.Application/Shop.Application.csproj src/Shop.Application/
COPY src/Shop.Domain/Shop.Domain.csproj src/Shop.Domain/
COPY src/Shop.Infrastructure/Shop.Infrastructure.csproj src/Shop.Infrastructure/
RUN dotnet restore src/Shop.Api/Shop.Api.csproj
COPY . .
WORKDIR /src/src/Shop.Api
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
USER root
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 libkrb5-3 && rm -rf /var/lib/apt/lists/*
USER app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Shop.Api.dll"]
