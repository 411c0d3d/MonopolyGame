# Stage 1 — Build the client and pre-compile JSX
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS client-build
WORKDIR /src
COPY MonopolyClient/ ./MonopolyClient/
RUN dotnet publish MonopolyClient/MonopolyClient.csproj -c Release -o /client-out

# Install Node.js and pre-compile all JSX to plain JS
# Eliminates runtime babel-standalone from the browser entirely
RUN apt-get update && apt-get install -y nodejs npm && \
    npm install -g @babel/cli @babel/preset-react

# Compile and minify all JS files in wwwroot
RUN babel /client-out/wwwroot \
    --out-dir /client-out/wwwroot \
    --presets @babel/preset-react \
    --extensions ".js" \
    --no-babelrc \
    --minified

# Strip type="text/babel" from index.html — no longer needed at runtime
RUN sed -i 's/ type="text\/babel"//g' /client-out/wwwroot/index.html

# Remove babel-standalone script tag from index.html
RUN sed -i '/babel-standalone/d' /client-out/wwwroot/index.html

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

# Merge client wwwroot into server wwwroot
# The server's UseStaticFiles will serve these automatically
COPY --from=client-build /client-out/wwwroot ./wwwroot

EXPOSE 8080

ENTRYPOINT ["dotnet", "MonopolyServer.dll"]