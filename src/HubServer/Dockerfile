FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Packages.props", "nuget.config", "./"]
COPY ["src/Contracts/Contracts.csproj", "src/Contracts/"]
COPY ["src/ServiceDefaults/ServiceDefaults.csproj", "src/ServiceDefaults/"]
COPY ["src/HubServer/HubServer.csproj", "src/HubServer/"]
RUN dotnet restore "src/HubServer/HubServer.csproj"
COPY src/Contracts/ src/Contracts/
COPY src/ServiceDefaults/ src/ServiceDefaults/
COPY src/HubServer/ src/HubServer/
RUN dotnet publish "src/HubServer/HubServer.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV GrpcPort=5001
EXPOSE 8080
EXPOSE 5001
ENTRYPOINT ["dotnet", "HubServer.dll"]
