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
ENV "Data:account-name"="amfsplunk5282ba5c"
ENV "Data:account-key"="deMSyI8GuwTjCLYT9YZrec9iY+5k0Scyn+TzGmFDgywfzO6B0QDNb62KBEsz6lHfkbwbBDr/z8t/8SyWTBfQIA=="
ENV "Azure:Tenant"="b8b4c61c-f1ca-4aff-a0bd-9c6f01c3eca5"
ENV "Azure:Subscription"="8b1050f9-c9e2-4886-b63b-0b891538759e"
ENV "Azure:ClientSecret"="TrZ*ghK.]:J53jb6o0_unTWiQRVn?sfT"
ENV "Azure:ClientId"="5e95f33e-1ba8-46ab-a432-280e8e8390d9"
ENV "Consumer:splunk-token": "d626ee56-8886-45d1-ae67-f154a207da60"
ENV "Consumer:splunk-cert": "188BD616F21F558C1F0E06749BAA75A46FB20AB8"
ENTRYPOINT ["dotnet", "splunk-eventhubs.dll"]