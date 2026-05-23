FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Stock.API/Stock.API.csproj Stock.API/
RUN dotnet restore Stock.API/Stock.API.csproj

COPY src/Stock.API/ Stock.API/
RUN dotnet publish Stock.API/Stock.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Stock.API.dll"]
