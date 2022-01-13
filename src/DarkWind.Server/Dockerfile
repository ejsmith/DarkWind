FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["src/DarkWind.Server/DarkWind.Server.csproj", "src/DarkWind.Server/"]
COPY ["src/DarkWind.Shared/DarkWind.Shared.csproj", "src/DarkWind.Shared/"]
COPY ["src/DarkWind.Client/DarkWind.Client.csproj", "src/DarkWind.Client/"]
RUN dotnet restore "src/DarkWind.Server/DarkWind.Server.csproj"
COPY . .
WORKDIR "/src/src/DarkWind.Server"
RUN dotnet build "DarkWind.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DarkWind.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DarkWind.Server.dll"]