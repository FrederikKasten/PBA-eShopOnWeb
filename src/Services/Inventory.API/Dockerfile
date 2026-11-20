FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Packages.props global.json ./
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Services/Inventory.API/Inventory.API.csproj src/Services/Inventory.API/
RUN dotnet restore src/Services/Inventory.API/Inventory.API.csproj

COPY src/Contracts/ src/Contracts/
COPY src/Services/Inventory.API/ src/Services/Inventory.API/
RUN dotnet publish src/Services/Inventory.API/Inventory.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Inventory.API.dll"]