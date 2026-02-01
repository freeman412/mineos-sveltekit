# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["MineOS.Api/MineOS.Api.csproj", "MineOS.Api/"]
COPY ["MineOS.Application/MineOS.Application.csproj", "MineOS.Application/"]
COPY ["MineOS.Domain/MineOS.Domain.csproj", "MineOS.Domain/"]
COPY ["MineOS.Infrastructure/MineOS.Infrastructure.csproj", "MineOS.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "MineOS.Api/MineOS.Api.csproj"

# Copy everything else
COPY . .

# Build
WORKDIR "/src/MineOS.Api"
RUN dotnet build "MineOS.Api.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "MineOS.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

# Install required packages
RUN apt-get update -o Acquire::Check-Valid-Until=false && apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    git \
    gnupg \
    iproute2 \
    procps \
    rdiff-backup \
    rsync \
    screen \
    tar \
    util-linux \
    wget \
    && mkdir -p /etc/apt/keyrings \
    && wget -qO- https://packages.adoptium.net/artifactory/api/gpg/key/public \
       | gpg --dearmor -o /etc/apt/keyrings/adoptium.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/adoptium.gpg] https://packages.adoptium.net/artifactory/deb $(. /etc/os-release && echo $VERSION_CODENAME) main" \
       > /etc/apt/sources.list.d/adoptium.list \
    && apt-get update -o Acquire::Check-Valid-Until=false \
    && apt-get install -y --no-install-recommends temurin-21-jdk temurin-8-jre \
    && rm -rf /var/lib/apt/lists/*

# Default to JDK 21 for toolchains like BuildTools; servers can still target other Java versions.
ENV JAVA_HOME=/usr/lib/jvm/temurin-21-jdk-amd64
ENV PATH="${JAVA_HOME}/bin:${PATH}"

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

# Version label (passed from docker-compose or CI)
ARG MINEOS_IMAGE_TAG=latest
ENV MINEOS_IMAGE_TAG=${MINEOS_IMAGE_TAG}

# Expose API port
EXPOSE 5078

# Run as root so we can use setpriv to drop privileges when starting Minecraft servers
ENTRYPOINT ["dotnet", "MineOS.Api.dll"]
