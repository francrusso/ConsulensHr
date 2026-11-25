# Immagine dell'app web per l'app di prova online (Render, piano gratuito: vedi render.yaml e docs/sviluppo.md).
# Si costruisce dalla radice del repository: docker build -t consulens-hr .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Prima i soli file di progetto: il restore resta in cache finché non cambiano i pacchetti.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/CL.Domain/CL.Domain.csproj src/CL.Domain/
COPY src/CL.Infrastructure/CL.Infrastructure.csproj src/CL.Infrastructure/
COPY src/CL.Web/CL.Web.csproj src/CL.Web/
RUN dotnet restore src/CL.Web/CL.Web.csproj
COPY src/ src/
RUN dotnet publish src/CL.Web/CL.Web.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# TZ: "oggi", scadenze e calendari all'ora italiana (il container sarebbe in UTC).
# FORWARDEDHEADERS: l'HTTPS lo gestisce il proxy di Render; così l'app sa che la richiesta era https (cookie sicuri, niente redirect).
# gcServer=0: meno memoria, il piano gratuito ha 512 MB.
ENV TZ=Europe/Rome \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0
USER $APP_UID
# Porta indicata dall'hosting in PORT (Render: 10000). Indirizzo pubblico dei link: App__UrlPubblico se impostato,
# altrimenti quello che Render assegna al servizio (RENDER_EXTERNAL_URL).
ENTRYPOINT ["/bin/sh", "-c", "export ASPNETCORE_HTTP_PORTS=\"${PORT:-8080}\" App__UrlPubblico=\"${App__UrlPubblico:-$RENDER_EXTERNAL_URL}\"; exec dotnet CL.Web.dll"]
