FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        build-essential \
        ca-certificates \
        curl \
        file \
        git \
        jq \
        libasound2 \
        libasound2-dev \
        libgl1-mesa-dri \
        libglu1-mesa \
        libopenal-dev \
        libsdl2-dev \
        patchelf \
        python3 \
        strace \
        xauth \
        unzip \
        xvfb \
        zip \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace
