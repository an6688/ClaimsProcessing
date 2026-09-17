FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/Claims.Api/Claims.Api.csproj
RUN dotnet publish src/Claims.Api/Claims.Api.csproj -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Claims.Api.dll"]
