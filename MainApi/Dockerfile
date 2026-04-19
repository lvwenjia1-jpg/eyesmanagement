FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MainApi/MainApi.csproj MainApi/
COPY MainApi/Program.cs MainApi/
COPY MainApi/appsettings.json MainApi/
COPY MainApi/appsettings.Development.json MainApi/
COPY MainApi/Contracts MainApi/Contracts
COPY MainApi/Controllers MainApi/Controllers
COPY MainApi/Data MainApi/Data
COPY MainApi/Domain MainApi/Domain
COPY MainApi/Options MainApi/Options
COPY MainApi/Properties MainApi/Properties
COPY MainApi/Services MainApi/Services
COPY MainApi/Dasbord MainApi/Dasbord
RUN dotnet restore "./MainApi/MainApi.csproj" -p:DisableImplicitNuGetFallbackFolder=true
RUN dotnet publish "./MainApi/MainApi.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=build /src/MainApi/Dasbord ./Dasbord
EXPOSE 8080

ENTRYPOINT ["dotnet", "MainApi.dll"]
