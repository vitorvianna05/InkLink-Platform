$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../../..").Path
$envFile = Join-Path $repoRoot '.env.local'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Run Initialize-Local.ps1 first.' }
# Named volumes may copy an image-owned root-only Erlang cookie. Preserve its content and repair only its owner/mode.
docker compose --env-file $envFile -f "$repoRoot/src/backend/compose.yaml" run --rm --no-deps --user 0 --entrypoint sh rabbitmq -ec 'if [ -f /var/lib/rabbitmq/.erlang.cookie ]; then chown rabbitmq:rabbitmq /var/lib/rabbitmq/.erlang.cookie; chmod 600 /var/lib/rabbitmq/.erlang.cookie; fi'
if ($LASTEXITCODE -ne 0) { throw 'Broker cookie preparation failed.' }

docker compose --env-file $envFile -f "$repoRoot/src/backend/compose.yaml" up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'Database startup failed.' }
docker compose --env-file $envFile -f "$repoRoot/src/backend/compose.yaml" exec -T rabbitmq sh /opt/inklink/configure-broker.sh
if ($LASTEXITCODE -ne 0) { throw 'Broker user configuration failed.' }
