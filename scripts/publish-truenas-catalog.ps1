param(
    [string]$Remote = "https://github.com/freeman412/mineos-sveltekit-truenas-catalog.git",
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"
$catalogPath = "deployments/truenas/catalog"
$splitBranch = "truenas-catalog"

if (-not (Test-Path $catalogPath)) {
    throw "Catalog path not found: $catalogPath"
}

git subtree split -P $catalogPath -b $splitBranch
git push $Remote $splitBranch:$Branch
