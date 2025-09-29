# Use the official .NET 8.0 runtime as a parent image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Use the official .NET 8.0 SDK as a build image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore as distinct layers
COPY ["DocumentProcessingAPI.API/DocumentProcessingAPI.API.csproj", "DocumentProcessingAPI.API/"]
COPY ["DocumentProcessingAPI.Core/DocumentProcessingAPI.Core.csproj", "DocumentProcessingAPI.Core/"]
COPY ["DocumentProcessingAPI.Infrastructure/DocumentProcessingAPI.Infrastructure.csproj", "DocumentProcessingAPI.Infrastructure/"]

RUN dotnet restore "DocumentProcessingAPI.API/DocumentProcessingAPI.API.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/DocumentProcessingAPI.API"
RUN dotnet build "DocumentProcessingAPI.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DocumentProcessingAPI.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create uploads directory
RUN mkdir -p /app/uploads

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "DocumentProcessingAPI.API.dll"]