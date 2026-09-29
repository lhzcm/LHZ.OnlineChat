# ===== 构建阶段 =====
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先复制解决方案级配置与全部项目文件，利用层缓存（依赖未变则跳过 restore）
COPY nuget.config Directory.Build.props ./
COPY LHZ.OnlineChat.Domain/LHZ.OnlineChat.Domain.csproj LHZ.OnlineChat.Domain/
COPY LHZ.OnlineChat.Application/LHZ.OnlineChat.Application.csproj LHZ.OnlineChat.Application/
COPY LHZ.OnlineChat.Infrastructure/LHZ.OnlineChat.Infrastructure.csproj LHZ.OnlineChat.Infrastructure/
COPY LHZ.OnlineChat.Server/LHZ.OnlineChat.Server.csproj LHZ.OnlineChat.Server/
RUN dotnet restore LHZ.OnlineChat.Server/LHZ.OnlineChat.Server.csproj

# 再复制源码（四层）
COPY LHZ.OnlineChat.Domain/ LHZ.OnlineChat.Domain/
COPY LHZ.OnlineChat.Application/ LHZ.OnlineChat.Application/
COPY LHZ.OnlineChat.Infrastructure/ LHZ.OnlineChat.Infrastructure/
COPY LHZ.OnlineChat.Server/ LHZ.OnlineChat.Server/
RUN dotnet publish LHZ.OnlineChat.Server/LHZ.OnlineChat.Server.csproj -c Release -o /app/publish --no-restore

# ===== 运行阶段 =====
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "LHZ.OnlineChat.Server.dll"]
