# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["mineos-sveltekit.sln", "./"]
COPY ["apps/MineOS.Api/MineOS.Api.csproj", "apps/MineOS.Api/"]
COPY ["apps/MineOS.Application/MineOS.Application.csproj", "apps/MineOS.Application/"]
COPY ["apps/MineOS.Domain/MineOS.Domain.csproj", "apps/MineOS.Domain/"]
COPY ["apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj", "apps/MineOS.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "apps/MineOS.Api/MineOS.Api.csproj"

# Copy everything else
COPY . .

# Build
WORKDIR "/src/apps/MineOS.Api"
RUN dotnet build "MineOS.Api.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "MineOS.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

# Install required packages
RUN apt-get update && apt-get install -y \
    screen \
    openjdk-21-jre-headless \
    curl \
    util-linux \
    procps \
    rsync \
    && rm -rf /var/lib/apt/lists/*

# Create minecraft user with UID 1000 and GID 1000
# This matches the default Host__OwnerUid and Host__OwnerGid settings
# This is a regular, unprivileged user that will run Minecraft servers
RUN groupadd -g 1000 minecraft && \
    useradd -u 1000 -g 1000 -m -s /bin/bash minecraft

# Run API as root to allow su for user switching
WORKDIR /app
COPY --from=publish /app/publish .

# Create directory structure
# Keep /var/games/minecraft root-owned for security
# Only specific server directories will be chowned to minecraft user when created
RUN mkdir -p /var/games/minecraft/servers \
    /var/games/minecraft/profiles \
    /var/games/minecraft/backups \
    /var/games/minecraft/archives \
    /var/games/minecraft/imports \
    /var/games/minecraft/logs \
    /app/data && \
    chmod 755 /var/games/minecraft && \
    chmod 700 /var/games/minecraft/logs && \
    chown -R 1000:1000 /app/data

# Expose API port
EXPOSE 5078

# Run as root so we can use setpriv to drop privileges when starting Minecraft servers
ENTRYPOINT ["dotnet", "MineOS.Api.dll"]
