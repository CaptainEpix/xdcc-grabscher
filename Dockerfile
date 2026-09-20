# Build XG with modern Mono
FROM mono:6.12 AS build

WORKDIR /src

# XG predates PackageReference, so restore its packages with NuGet.exe.
ADD https://dist.nuget.org/win-x86-commandline/latest/nuget.exe /tmp/nuget.exe

COPY . .

RUN mono /tmp/nuget.exe restore XG.sln \
      -PackagesDirectory packages \
      -NonInteractive \
 && xbuild XG.Application/XG.Application.csproj \
      /t:Rebuild \
      /p:Configuration=Build.Mono \
 && cp packages/Nowin.0.12.1.0/lib/net45/Nowin.dll \
      XG.Application/bin/Build.Mono/Nowin.dll


# Obtain Debian's Mono-compatible db4o build.
#
# The NuGet db4o binary calls the Windows-only FlushFileBuffers API
# when run under modern Mono. Debian's db4o package contains a
# Mono-compatible build of the same library.
FROM debian:bookworm-slim AS db4o

RUN apt-get update \
 && apt-get install -y --no-install-recommends libdb4o8.0-cil \
 && find /usr/lib -type f -name 'Db4objects.Db4o.dll' \
      -exec cp '{}' /Db4objects.Db4o.dll ';' \
 && test -f /Db4objects.Db4o.dll \
 && rm -rf /var/lib/apt/lists/*


# Runtime
FROM mono:6.12

WORKDIR /app

COPY --from=build /src/XG.Application/bin/Build.Mono/ /app/

# Replace the NuGet db4o DLL with the Mono-compatible Debian build.
COPY --from=db4o /Db4objects.Db4o.dll /app/Db4objects.Db4o.dll

ENV HOME=/config

EXPOSE 5556

VOLUME ["/config"]

# XG deliberately refuses to run as root.
# 99:100 is nobody:users on Unraid.
USER 99:100

ENTRYPOINT ["mono", "XG.Application.exe"]
