# Stage 1 — Build the client and pre-compile JSX
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS client-build
WORKDIR /src
COPY MonopolyClient/ ./MonopolyClient/
RUN dotnet publish MonopolyClient/MonopolyClient.csproj -c Release -o /client-out

# Install Node.js 20 via NodeSource — apt version is too old
RUN apt-get update && apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs && \
    node --version && npm --version && \
    npm install -g @babel/cli @babel/preset-react @babel/core

# Verify Babel works on one file before compiling everything
RUN babel /client-out/wwwroot/app.js \
    --presets @babel/preset-react \
    --no-babelrc \
    --out-file /tmp/app-test.js && \
    echo "=== Babel test succeeded ===" && \
    head -3 /tmp/app-test.js

# Compile and minify all JS files in wwwroot in place
RUN babel /client-out/wwwroot \
    --out-dir /client-out/wwwroot \
    --presets @babel/preset-react \
    --extensions ".js" \
    --no-babelrc \
    --minified && \
    echo "=== Babel compilation complete ==="

# Strip type="text/babel" from all script tags in index.html
RUN sed -i 's/ type="text\/babel"//g' /client-out/wwwroot/index.html

# Remove babel-standalone CDN script tag from index.html
RUN sed -i '/babel-standalone/d' /client-out/wwwroot/index.html

# Verify babel-standalone is gone
RUN grep -c "babel-standalone" /client-out/wwwroot/index.html || echo "=== babel-standalone removed ==="

# Stage 2 — Build the server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-build
WORKDIR /src
COPY MonopolyServer/ ./MonopolyServer/
RUN dotnet publish MonopolyServer/MonopolyServer.csproj -c Release -o /server-out

# Stage 3 — Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy server publish output
COPY --from=server-build /server-out ./

# Merge compiled client wwwroot into server wwwroot
COPY --from=client-build /client-out/wwwroot ./wwwroot

EXPOSE 8080

ENTRYPOINT ["dotnet", "MonopolyServer.dll"]