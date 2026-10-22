FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Lore.Contracts/Lore.Contracts.csproj src/Lore.Contracts/
COPY src/Lore.Api/Lore.Api.csproj src/Lore.Api/
RUN dotnet restore src/Lore.Api/Lore.Api.csproj
COPY src/Lore.Contracts/ src/Lore.Contracts/
COPY src/Lore.Api/ src/Lore.Api/
RUN dotnet publish src/Lore.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# ffmpeg: video poster frames + ffprobe metadata. No transcoding in V1.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ffmpeg tzdata \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /db /data && chown app:app /db /data
WORKDIR /app
COPY --from=build /app .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lore.Api.dll"]
