FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Smes.Api/Smes.Api.csproj -c Release -o /app/publish /p:AllowMissingPrunePackageData=true /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV TZ=Asia/Shanghai
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Smes.Api.dll"]
