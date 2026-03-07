# Stage 1 — Compile client JSX using dedicated Node image
FROM node:20-alpine AS client-build
WORKDIR /client
# Copy package files first for better layer caching
COPY MonopolyClient/package*.json ./
RUN npm install
# Copy source files
COPY MonopolyClient/wwwroot ./wwwroot
# Compile JSX to plain JS, output to wwwroot-compiled
RUN npm run build
# Copy vendor lib, global css dir, and co-located CSS files (e.g. animation/, dice3d/)
RUN cp -r wwwroot/lib wwwroot-compiled/lib && \
    cp -r wwwroot/css wwwroot-compiled/css && \
    cp wwwroot/index.html wwwroot-compiled/index.html && \
    cp wwwroot/favicon.ico wwwroot-compiled/favicon.ico 2>/dev/null || true && \
    find wwwroot -name "*.css" -not -path "wwwroot/css/*" | \
        while read f; do \
            dest="wwwroot-compiled/${f#wwwroot/}"; \
            mkdir -p "$(dirname "$dest")"; \
            cp "$f" "$dest"; \
        done
# Strip type="text/babel" and remove babel-standalone from index.html
RUN sed -i 's/ type="text\/babel"//g' wwwroot-compiled/index.html && \
    sed -i '/babel-standalone/d' wwwroot-compiled/index.html && \
    echo "=== Client build complete ==="

# Stage 2 — Build server with compiled client assets
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-build
WORKDIR /src
# Copy compiled client wwwroot into server project before publish
COPY --from=client-build /client/wwwroot-compiled ./MonopolyServer/wwwroot
COPY MonopolyServer/ ./MonopolyServer/
RUN dotnet publish MonopolyServer/MonopolyServer.csproj -c Release -o /server-out

# Stage 3 — Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=server-build /server-out ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "MonopolyServer.dll"]