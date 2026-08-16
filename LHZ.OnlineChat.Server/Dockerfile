# ===== 构建阶段 =====
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先复制项目文件与 NuGet 配置，利用层缓存
COPY LHZ.OnlineChat.Server/nuget.config LHZ.OnlineChat.Server/
COPY LHZ.OnlineChat.Server/LHZ.OnlineChat.Server.csproj LHZ.OnlineChat.Server/
RUN dotnet restore LHZ.OnlineChat.Server/LHZ.OnlineChat.Server.csproj

COPY LHZ.OnlineChat.Server/ LHZ.OnlineChat.Server/
RUN dotnet publish LHZ.OnlineChat.Server/LHZ.OnlineChat.Server.csproj -c Release -o /app/publish --no-restore

# ===== 运行阶段 =====
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "LHZ.OnlineChat.Server.dll"]
