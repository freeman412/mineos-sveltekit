# TrueNAS Electric Eel (24.10) Docker App Catalog (MineOS)

This scaffold adds a TrueNAS Apps catalog entry for MineOS using Docker compose
with a questions.yaml file for UI configuration.

Layout:
- catalog/apps/mineos-sveltekit/app.yaml
- catalog/apps/mineos-sveltekit/versions/0.1.0/docker-compose.yaml
- catalog/apps/mineos-sveltekit/versions/0.1.0/questions.yaml

Notes:
- TrueNAS Apps require prebuilt images. Update the image values in
  questions.yaml or point them at your registry.
- PUBLIC_API_BASE_URL and ORIGIN must be reachable by client browsers.
- The Minecraft port range is exposed for both TCP and UDP.

Install flow (high level):
1) Build and push images to a registry.
2) Add this repo as a custom catalog in TrueNAS.
3) Install the "MineOS" app and fill out the questions.
