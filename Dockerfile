FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY [".", "./"]
RUN dotnet restore
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/out .

ENTRYPOINT ["sh", "-c", "exec dotnet Practica2AppCreditos.dll --urls http://0.0.0.0:${PORT}"]