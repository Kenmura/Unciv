#!/bin/bash
# Container health check (T374): GET /healthz over the relay's HTTP side; with UNCIV_RELAY_NO_HTTP on, only a TCP connect to the tunnel port.
# The runtime image has bash but no curl, so this uses bash's /dev/tcp.
no_http="${UNCIV_RELAY_NO_HTTP:-}"
if [[ -n $no_http && $no_http != 0 && ${no_http,,} != false ]]; then
    exec 3<>"/dev/tcp/127.0.0.1/${UNCIV_RELAY_TUNNEL_PORT:-47830}"
    exit 0
fi
exec 3<>"/dev/tcp/127.0.0.1/${UNCIV_RELAY_HTTP_PORT:-47831}"
printf 'GET /healthz HTTP/1.0\r\n\r\n' >&3
read -r status <&3
[[ $status == *' 200 '* ]]
