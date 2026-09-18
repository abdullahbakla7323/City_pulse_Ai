# ==============================================
# CityPulse AI - Dockerfile
# .NET 10 + PostgreSQL/PostGIS Desktop App
# Multi-stage build for optimized image size
# ==============================================

# ── Stage 1: Build ────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies first (layer caching)
COPY CityPulseAI.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime ──────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install PostGIS runtime dependencies
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    libpq-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy published output from build stage
COPY --from=build /app/publish .

# Create directories for runtime file operations
RUN mkdir -p wwwroot/temp_reports wwwroot/uploads

# Expose HTTP port
EXPOSE 8080

# Set environment to Production
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

# Note: Photino (desktop window) is DISABLED in container mode.
# The app runs as a pure web server accessible via http://localhost:8080
ENTRYPOINT ["dotnet", "CityPulseAI.dll"]
