#!/usr/bin/env bash
# Serves the example registry over HTTP so it can be used with BinPm.
#
#   ./examples/serve-registry.sh            # serves on http://127.0.0.1:8731
#   ./examples/serve-registry.sh 9000       # serve on a different port
#
# Then point BinPm at it:
#
#   binpm install ripgrep --registry example
#
# with this entry in ~/.config/binpm/config.json:
#
#   { "registries": [ { "name": "example", "type": "http", "url": "http://127.0.0.1:8731" } ] }

set -euo pipefail

PORT="${1:-8731}"
DIRECTORY="$(cd "$(dirname "${BASH_SOURCE[0]}")/registry" && pwd)"

echo "Serving $DIRECTORY on http://127.0.0.1:$PORT"
exec python3 -m http.server "$PORT" --bind 127.0.0.1 --directory "$DIRECTORY"
