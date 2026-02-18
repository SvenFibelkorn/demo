# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY dotnet/dotnet.csproj dotnet/
RUN dotnet restore dotnet/dotnet.csproj

COPY dotnet/ dotnet/
RUN dotnet publish dotnet/dotnet.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

RUN apt-get update \
	&& apt-get install -y --no-install-recommends libgomp1 \
	&& rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .
COPY models/ /models/

EXPOSE 8080

ENTRYPOINT ["dotnet", "dotnet.dll"]
