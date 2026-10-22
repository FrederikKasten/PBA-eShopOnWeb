
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Packages.props global.json ./
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Services/Ordering.API/Ordering.API.csproj src/Services/Ordering.API/
RUN dotnet restore src/Services/Ordering.API/Ordering.API.csproj

COPY src/Contracts/ src/Contracts/
COPY src/Services/Ordering.API/ src/Services/Ordering.API/
RUN dotnet publish src/Services/Ordering.API/Ordering.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Ordering.API.dll"]
