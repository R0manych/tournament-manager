FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем csproj отдельным слоем для кэширования restore
COPY Zettel.Domain/Zettel.Domain.csproj             Zettel.Domain/
COPY Zettel.Infrastructure/Zettel.Infrastructure.csproj Zettel.Infrastructure/
COPY Zettel.Api/Zettel.Api.csproj                   Zettel.Api/
RUN dotnet restore Zettel.Api/Zettel.Api.csproj

# Копируем весь исходник и публикуем
COPY . .
RUN dotnet publish Zettel.Api/Zettel.Api.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Zettel.Api.dll"]
