FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["src/ECommerce.Worker/ECommerce.Worker.csproj", "src/ECommerce.Worker/"]
RUN dotnet restore "src/ECommerce.Worker/ECommerce.Worker.csproj"
COPY . .
RUN dotnet publish "src/ECommerce.Worker/ECommerce.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ECommerce.Worker.dll"]
