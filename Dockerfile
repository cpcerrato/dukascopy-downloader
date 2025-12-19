FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true -o /out

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0 AS runtime
WORKDIR /app
COPY --from=build /out/dukascopy-downloader .
ENTRYPOINT ["/app/dukascopy-downloader"]
