# The SDK stage runs natively on the build machine and cross-compiles for the target platform,
# so multi-arch builds (amd64/arm64) need no emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet publish EBusToInflux -c Release -a $TARGETARCH -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "EBusToInflux.dll"]
