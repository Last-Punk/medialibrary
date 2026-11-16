FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY MediaLibrary.Api/*.csproj MediaLibrary.Api/
COPY MediaLibrary.Core/*.csproj MediaLibrary.Core/
COPY MediaLibrary.Migrations.Sqlite/*.csproj MediaLibrary.Migrations.Sqlite/
COPY MediaLibrary.Migrations.Postgres/*.csproj MediaLibrary.Migrations.Postgres/
RUN dotnet restore MediaLibrary.Api/MediaLibrary.Api.csproj
COPY . .
RUN dotnet publish MediaLibrary.Api/MediaLibrary.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "MediaLibrary.Api.dll"]
