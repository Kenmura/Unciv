#!/bin/bash
# Container health check (T374): GET /healthz over the relay's HTTP side; with UNCIV_RELAY_NO_HTTP on, only a TCP connect to the tunnel port.
# The runtime image has bash but no curl, so this uses bash's /dev/tcp.
# With UNCIV_RELAY_HTTP_TLS the HTTP side speaks HTTPS, which bash cannot: then it only checks that the HTTP port accepts a connection.
on() { [[ -n $1 && $1 != 0 && ${1,,} != false ]]; }
if on "${UNCIV_RELAY_NO_HTTP:-}"; then
    exec 3<>"/dev/tcp/127.0.0.1/${UNCIV_RELAY_TUNNEL_PORT:-47830}" || exit 1
    exit 0
fi
if on "${UNCIV_RELAY_HTTP_TLS:-}"; then
    exec 3<>"/dev/tcp/127.0.0.1/${UNCIV_RELAY_HTTP_PORT:-47831}" || exit 1
    exit 0
fi
exec 3<>"/dev/tcp/127.0.0.1/${UNCIV_RELAY_HTTP_PORT:-47831}" || exit 1
printf 'GET /healthz HTTP/1.0\r\n\r\n' >&3
read -r status <&3
[[ $status == *' 200 '* ]]
