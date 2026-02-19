# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY scheduler/scheduler.csproj scheduler/
RUN dotnet restore scheduler/scheduler.csproj

COPY scheduler/ scheduler/
RUN dotnet publish scheduler/scheduler.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV DOTNET_ENVIRONMENT=Production

COPY --from=build /app/publish .
COPY corpus/ /corpus/
COPY models/ /models/

RUN if [ -f /app/runtimes/linux-x64/native/libonnxruntime.so ] && [ ! -e /app/runtimes/linux-x64/native/libonnxruntime.dll.so ]; then \
			ln -s /app/runtimes/linux-x64/native/libonnxruntime.so /app/runtimes/linux-x64/native/libonnxruntime.dll.so; \
		fi

ENTRYPOINT ["dotnet", "scheduler.dll"]
