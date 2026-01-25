# ---- build ----
FROM node:20-bookworm AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . ./

ARG PUBLIC_BUILD_ID=dev
ARG PUBLIC_MINECRAFT_HOST
ENV PUBLIC_BUILD_ID=$PUBLIC_BUILD_ID
ENV PUBLIC_MINECRAFT_HOST=$PUBLIC_MINECRAFT_HOST

# Build
RUN npm run build

# Show what got built (helps debugging)
RUN echo "=== /app contents ===" && ls -la && \
    echo "=== /app/build contents ===" && (ls -la build || true) && \
    echo "=== /app/.svelte-kit contents ===" && (ls -la .svelte-kit || true)

RUN npm prune --omit=dev


# ---- runtime ----
FROM node:20-bookworm AS runtime
WORKDIR /app
ENV NODE_ENV=production
ENV PORT=3000
ENV HOST=0.0.0.0

COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json ./package.json
COPY --from=build /app/build ./build

EXPOSE 3000

# Fail loud if the entrypoint isn't there
CMD ["bash", "-lc", "set -euxo pipefail; ls -la; ls -la build; node build/index.js"]
