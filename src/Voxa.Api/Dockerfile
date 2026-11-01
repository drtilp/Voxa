# Remote inference server. Build from the repository root:
#   CPU (default): docker build -f src/Voxa.Api/Dockerfile -t voxa-api .
#   GPU (CUDA 12): docker build -f src/Voxa.Api/Dockerfile --target gpu --build-arg VOXA_GPU=true -t voxa-api-gpu .
#   docker run -p 8080:8080 -e Voxa__ApiKey=<key> voxa-api      (GPU: add --gpus all)
# Models are baked into the image (~3 GB) in their own layer, below the app: a code change re-pushes only
# the small app layer, and the container starts without depending on Hugging Face.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS models
# false = smaller image without the optional models (server then runs without language ID / translation).
ARG WITH_LID=true
ARG WITH_TRANSLATION=true
ARG HF=https://huggingface.co
WORKDIR /models
RUN set -eux; \
    mkdir -p asr/parakeet-tdt-0.6b-v3 vad; \
    for f in encoder.int8.onnx decoder.int8.onnx joiner.int8.onnx tokens.txt; do \
      curl -fsSL -o asr/parakeet-tdt-0.6b-v3/$f $HF/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/$f; \
    done; \
    curl -fsSL -o vad/silero_vad.onnx https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx; \
    if [ "$WITH_LID" = true ]; then \
      mkdir -p lid/whisper-small; \
      for f in small-encoder.int8.onnx small-decoder.int8.onnx small-tokens.txt; do \
        curl -fsSL -o lid/whisper-small/$f $HF/csukuangfj/sherpa-onnx-whisper-small/resolve/main/$f; \
      done; \
    fi; \
    if [ "$WITH_TRANSLATION" = true ]; then \
      mkdir -p translation; \
      curl -fsSL -o translation/Hy-MT2-1.8B-Q8_0.gguf $HF/tencent/Hy-MT2-1.8B-GGUF/resolve/main/Hy-MT2-1.8B-Q8_0.gguf; \
    fi

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# true = llama.cpp CUDA backend (see Voxa.Api.csproj)
ARG VOXA_GPU=false
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Voxa.Core/Voxa.Core.csproj src/Voxa.Core/
COPY src/Voxa.Engines.SherpaOnnx/Voxa.Engines.SherpaOnnx.csproj src/Voxa.Engines.SherpaOnnx/
COPY src/Voxa.Translators.Llama/Voxa.Translators.Llama.csproj src/Voxa.Translators.Llama/
COPY src/Voxa.Api/Voxa.Api.csproj src/Voxa.Api/
RUN dotnet restore src/Voxa.Api -r linux-x64 -p:VoxaGpu=$VOXA_GPU
COPY src/ src/
RUN dotnet publish src/Voxa.Api -c Release -r linux-x64 --self-contained false --no-restore -p:VoxaGpu=$VOXA_GPU -o /app

# /v1/ingest: yt-dlp finds the audio behind a page URL; YouTube needs a JavaScript runtime for it (Deno, yt-dlp's
# default). Standalone binaries, pinned; bump YTDLP_VERSION when sites change (yt-dlp releases often).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS tools
ARG YTDLP_VERSION=2026.10.21
ARG DENO_VERSION=v2.7.0
RUN set -eux; apt-get update; apt-get install -y --no-install-recommends unzip; mkdir /tools; \
    curl -fsSL -o /tools/yt-dlp https://github.com/yt-dlp/yt-dlp/releases/download/$YTDLP_VERSION/yt-dlp_linux; \
    curl -fsSL -o /tmp/deno.zip https://github.com/denoland/deno/releases/download/$DENO_VERSION/deno-x86_64-unknown-linux-gnu.zip; \
    unzip -q /tmp/deno.zip -d /tools; chmod +x /tools/yt-dlp /tools/deno

# sherpa-onnx has no CUDA NuGet package: take the CUDA build of the same version from its GitHub release and
# replace the CPU libraries of the NuGet package with it (same C API).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sherpa-gpu
ARG SHERPA_GPU=sherpa-onnx-v1.13.8-cuda-12.x-cudnn-9.x-onnxruntime1.28.2-linux-x64-gpu
RUN set -eux; apt-get update; apt-get install -y --no-install-recommends bzip2; mkdir /lib-gpu; \
    curl -fsSL https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.8/$SHERPA_GPU.tar.bz2 \
      | tar xj --strip-components=2 -C /lib-gpu $SHERPA_GPU/lib/libsherpa-onnx-c-api.so $SHERPA_GPU/lib/libonnxruntime.so \
          $SHERPA_GPU/lib/libonnxruntime_providers_shared.so $SHERPA_GPU/lib/libonnxruntime_providers_cuda.so

# GPU: CUDA 12 runtime + cuDNN 9 (needed by onnxruntime and llama.cpp), .NET copied in. The host driver only has to
# be new enough for CUDA 12 (Azure Container Apps T4: driver 570+).
FROM nvidia/cuda:12.8.1-cudnn-runtime-ubuntu24.04 AS gpu
COPY --from=mcr.microsoft.com/dotnet/aspnet:10.0 /usr/share/dotnet /usr/share/dotnet
RUN apt-get update && apt-get install -y --no-install-recommends libicu74 libgomp1 ca-certificates ffmpeg \
    && rm -rf /var/lib/apt/lists/* && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
COPY --from=tools /tools/ /usr/local/bin/
COPY --from=models /models /models
WORKDIR /app
COPY --from=build /app .
COPY --from=sherpa-gpu /lib-gpu/ /app/
ENV Voxa__ModelsFolder=/models Voxa__Provider=cuda ASPNETCORE_HTTP_PORTS=8080 LD_LIBRARY_PATH=/app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Voxa.Api.dll"]

# CPU (default target, keep it last)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS cpu
# llama.cpp CPU backend needs OpenMP; without it LLamaSharp reports "native library cannot be loaded".
# ffmpeg decodes the media of /v1/ingest.
RUN apt-get update && apt-get install -y --no-install-recommends libgomp1 ffmpeg && rm -rf /var/lib/apt/lists/*
COPY --from=tools /tools/ /usr/local/bin/
COPY --from=models /models /models
WORKDIR /app
COPY --from=build /app .
ENV Voxa__ModelsFolder=/models
EXPOSE 8080
ENTRYPOINT ["dotnet", "Voxa.Api.dll"]
