FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем csproj отдельным слоем для кэширования restore
COPY TournamentManager.Domain/TournamentManager.Domain.csproj             TournamentManager.Domain/
COPY TournamentManager.Infrastructure/TournamentManager.Infrastructure.csproj TournamentManager.Infrastructure/
COPY TournamentManager.Api/TournamentManager.Api.csproj                   TournamentManager.Api/
RUN dotnet restore TournamentManager.Api/TournamentManager.Api.csproj

# Копируем весь исходник и публикуем
COPY . .
RUN dotnet publish TournamentManager.Api/TournamentManager.Api.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TournamentManager.Api.dll"]
