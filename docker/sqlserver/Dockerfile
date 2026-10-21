# SQL Server 2022 with full-text search, for local development only.
#
# The keyword search needs full-text search, and the stock Linux image does not carry it: the
# `mssql-server-fts` package lives in Microsoft's own apt repository, which the image does not
# configure either. Both are set up here, so the install happens once when the image is built rather
# than on every `docker compose up`.
#
# Azure SQL Database has full-text search built in, so nothing like this is needed in production
# (see docs/deployment.md).
FROM mcr.microsoft.com/mssql/server:2022-latest

# The package install needs root; the server itself goes back to running as mssql at the end.
USER root

# The base image is Ubuntu 22.04 (jammy), which decides which repository list to fetch.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl gnupg ca-certificates \
 && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
      | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
 && echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/ubuntu/22.04/mssql-server-2022 jammy main" \
      > /etc/apt/sources.list.d/mssql-server-2022.list \
 && apt-get update \
 && ACCEPT_EULA=Y apt-get install -y --no-install-recommends mssql-server-fts \
 && apt-get purge -y --auto-remove curl gnupg \
 && rm -rf /var/lib/apt/lists/*

USER mssql
