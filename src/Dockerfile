FROM mcr.microsoft.com/dotnet/core/aspnet:2.2-stretch-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/core/sdk:2.2-stretch AS build
WORKDIR /src
COPY ["splunk-eventhubs.csproj", ""]
RUN dotnet restore "splunk-eventhubs.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "splunk-eventhubs.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "splunk-eventhubs.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "splunk-eventhubs.dll"]