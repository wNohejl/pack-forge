# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# g++ builds the native math kernel (packforge_eval); without it the app falls
# back to the managed evaluator, so this is an optimization, not a hard dep.
RUN apt-get update && apt-get install -y --no-install-recommends g++ && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY *.slnx ./
COPY src/PackForge.Core/*.csproj src/PackForge.Core/
COPY src/PackForge.Web/*.csproj src/PackForge.Web/
RUN dotnet restore src/PackForge.Web/PackForge.Web.csproj
COPY src/ src/
COPY native/ native/
RUN dotnet publish src/PackForge.Web/PackForge.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PackForge.Web.dll"]
