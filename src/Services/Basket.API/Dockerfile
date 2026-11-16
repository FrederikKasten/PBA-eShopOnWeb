FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Packages.props global.json ./
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Services/Basket.API/Basket.API.csproj src/Services/Basket.API/
RUN dotnet restore src/Services/Basket.API/Basket.API.csproj

COPY src/Contracts/ src/Contracts/
COPY src/Services/Basket.API/ src/Services/Basket.API/
RUN dotnet publish src/Services/Basket.API/Basket.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Basket.API.dll"]