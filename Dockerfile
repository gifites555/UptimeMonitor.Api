FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY UptimeMonitor.Api/UptimeMonitor.Api.csproj UptimeMonitor.Api/
RUN dotnet restore UptimeMonitor.Api/UptimeMonitor.Api.csproj
COPY UptimeMonitor.Api/ UptimeMonitor.Api/
RUN dotnet publish UptimeMonitor.Api/UptimeMonitor.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "UptimeMonitor.Api.dll"]

