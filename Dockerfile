# One image, three services and two tools. AP_SERVICE selects what runs.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src ./src
COPY samples ./samples
COPY tools ./tools
RUN for p in src/ControlApi/ControlApi.csproj:control src/PolicyExecutor/PolicyExecutor.csproj:executor \
             src/ContainmentWorker/ContainmentWorker.csproj:worker samples/LocalSlice/LocalSlice.csproj:demo \
             tools/DevToken/DevToken.csproj:devtoken; do \
      dotnet publish "${p%%:*}" -c Release -o "/out/${p##*:}" --nologo || exit 1; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:8.0
LABEL org.opencontainers.image.title="Agent Portifolio" \
      org.opencontainers.image.description="Capability wallet, deterministic authorisation, ledger auditing and containment for AI agents. Microsoft adapters in simulation mode." \
      org.opencontainers.image.source="https://github.com/epestana28/agent-portifolio" \
      org.opencontainers.image.licenses="Apache-2.0"
WORKDIR /app
COPY --from=build /out ./
COPY docker/entrypoint.sh /usr/local/bin/ap-entrypoint
RUN chmod 0755 /usr/local/bin/ap-entrypoint
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["ap-entrypoint"]
