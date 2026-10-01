# Imagen de la API (con el explorador web incluido) para desplegarla en un VPS con Docker, Render, etc.
# Se construye desde la raíz del repositorio:  docker build -t murcia-datos-api .

# --- Compilación -------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS compilar
ARG TARGETARCH
WORKDIR /origen

# Primero solo lo que decide las dependencias: mientras no cambien, la restauración de paquetes (lo más lento)
# se reutiliza de la caché de Docker aunque cambie el código.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/MurciaDatos.Datos/MurciaDatos.Datos.csproj src/MurciaDatos.Datos/
COPY src/MurciaDatos.Explorador/MurciaDatos.Explorador.csproj src/MurciaDatos.Explorador/
COPY src/MurciaDatos.Api/MurciaDatos.Api.csproj src/MurciaDatos.Api/
RUN dotnet restore src/MurciaDatos.Api/MurciaDatos.Api.csproj

COPY src ./src

# El explorador se publica por su cuenta: así sale con la página ya procesada (nombres de fichero con huella e
# importmap), que es lo que sirve la API. De las librerías nativas de DuckDB solo se deja la de la arquitectura de destino.
RUN dotnet publish src/MurciaDatos.Explorador/MurciaDatos.Explorador.csproj -c Release --no-restore -o /explorador
RUN ARQUITECTURA=$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) \
    && dotnet publish src/MurciaDatos.Api/MurciaDatos.Api.csproj -c Release --no-restore -p:UseAppHost=false -o /publicado \
    && rm -rf /publicado/wwwroot \
    && cp -r /explorador/wwwroot /publicado/wwwroot \
    && find /publicado/runtimes -mindepth 1 -maxdepth 1 ! -name "linux-$ARQUITECTURA" -exec rm -rf {} +

# --- Ejecución ---------------------------------------------------------------------------------
# Solo el entorno de ejecución de ASP.NET Core (sin SDK ni compiladores).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=compilar /publicado .

# Aquí se guardan las versiones de datos descargadas: así, tras un reinicio, la API sirve datos al instante aunque
# GitHub no responda. El usuario «app» (no administrador) ya viene creado en la imagen.
RUN mkdir /datos && chown $APP_UID /datos
VOLUME /datos
USER $APP_UID

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Datos__Directorio=/datos \
    DOTNET_gcServer=0
EXPOSE 8080

ENTRYPOINT ["dotnet", "MurciaDatos.Api.dll"]
