FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PartyBot.Api/PartyBot.Api.csproj PartyBot.Api/
RUN dotnet restore PartyBot.Api/PartyBot.Api.csproj
COPY PartyBot.Api/ PartyBot.Api/
RUN dotnet publish PartyBot.Api/PartyBot.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "PartyBot.Api.dll"]
