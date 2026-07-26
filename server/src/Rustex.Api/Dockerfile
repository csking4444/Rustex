FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Rustex.sln .
COPY src/Rustex.Api/Rustex.Api.csproj src/Rustex.Api/
COPY src/Rustex.Domain/Rustex.Domain.csproj src/Rustex.Domain/
COPY src/Rustex.Infrastructure/Rustex.Infrastructure.csproj src/Rustex.Infrastructure/
COPY tests/Rustex.Api.Tests/Rustex.Api.Tests.csproj tests/Rustex.Api.Tests/
RUN dotnet restore src/Rustex.Api/Rustex.Api.csproj

COPY . .
RUN dotnet publish src/Rustex.Api/Rustex.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN useradd --uid 5678 --user-group appuser
USER appuser

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Rustex.Api.dll"]
