FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY RaceRelay.csproj Wire.cs Program.cs ./
RUN dotnet publish RaceRelay.csproj -c Release -o /relay

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /relay
COPY --from=build /relay .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RaceRelay.dll"]
