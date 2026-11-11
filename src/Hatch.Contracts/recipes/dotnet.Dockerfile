# dotnet: the .NET SDK from Microsoft's dotnet-install.sh, which picks the architecture itself. A channel for a major or major.minor, an exact version for three components.
ARG DOTNET_VERSION=10.0
ENV DOTNET_ROOT=/usr/share/dotnet DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
RUN curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && case "$DOTNET_VERSION" in \
      *.*.*) flag=--version; value=$DOTNET_VERSION ;; \
      *.*) flag=--channel; value=$DOTNET_VERSION ;; \
      *) flag=--channel; value=$DOTNET_VERSION.0 ;; \
    esac \
 && bash /tmp/dotnet-install.sh "$flag" "$value" --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh \
 && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
