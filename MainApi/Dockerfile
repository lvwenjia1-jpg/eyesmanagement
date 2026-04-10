FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

COPY ["MainApi/MainApi.csproj", "MainApi/"]
COPY ["OrderTextTrainer.Core/OrderTextTrainer.Core.csproj", "OrderTextTrainer.Core/"]
RUN dotnet restore "MainApi/MainApi.csproj"

COPY . .
RUN dotnet publish "MainApi/MainApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "MainApi.dll"]
