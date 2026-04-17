FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy API project and referenced project files
COPY ["TradingSystem.Api.csproj", "./"]
COPY ["../TradingSystem.Core/TradingSystem.Core.csproj", "../TradingSystem.Core/"]
COPY ["../TradingSystem.Data/TradingSystem.Data.csproj", "../TradingSystem.Data/"]
COPY ["../TradingSystem.Indicators/TradingSystem.Indicators.csproj", "../TradingSystem.Indicators/"]

RUN dotnet restore "./TradingSystem.Api.csproj"

# Copy the rest of the API source
COPY . .

RUN dotnet publish "TradingSystem.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 80
EXPOSE 443

ENTRYPOINT ["dotnet", "TradingSystem.Api.dll"]