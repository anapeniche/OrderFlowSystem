FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Order.API/Order.API.csproj Order.API/
RUN dotnet restore Order.API/Order.API.csproj

COPY src/Order.API/ Order.API/
RUN dotnet publish Order.API/Order.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Order.API.dll"]

