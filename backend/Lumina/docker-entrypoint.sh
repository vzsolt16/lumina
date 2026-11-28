#!/bin/sh
set -eu

# Named volumes created by older/root containers may not yet be writable by
# the ASP.NET runtime user. Repair ownership before dropping privileges.
mkdir -p /app/data
chown -R app:app /app/data
exec gosu app dotnet /app/Lumina.dll
