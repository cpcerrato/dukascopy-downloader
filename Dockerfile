FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
ARG TARGETARCH
RUN dotnet publish src/DukascopyDownloader.Cli/DukascopyDownloader.Cli.csproj -c Release -r linux-${TARGETARCH} --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true -o /out

FROM gcr.io/distroless/cc-debian12:nonroot AS runtime
WORKDIR /app
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENV DOTNET_SUGGEST_DISABLE=1
COPY --from=build /out/dukascopy-downloader /app/dukascopy-downloader
ENTRYPOINT ["/app/dukascopy-downloader"]
