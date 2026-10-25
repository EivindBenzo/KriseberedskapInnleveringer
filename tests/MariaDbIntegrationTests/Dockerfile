FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src

COPY HeimevernetInnlevering1.csproj ./
COPY tests/MariaDbIntegrationTests/MariaDbIntegrationTests.csproj tests/MariaDbIntegrationTests/
RUN dotnet restore tests/MariaDbIntegrationTests/MariaDbIntegrationTests.csproj

COPY . .

ENTRYPOINT ["dotnet", "test", "tests/MariaDbIntegrationTests/MariaDbIntegrationTests.csproj", "--no-restore", "--configuration", "Release"]
