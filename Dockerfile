FROM node:22-alpine AS frontend
WORKDIR /src
COPY frontend-vue/package.json frontend-vue/package-lock.json ./frontend-vue/
RUN cd frontend-vue && npm ci
COPY frontend-vue ./frontend-vue
COPY src ./src
RUN cd frontend-vue && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PalOpsWeb.slnx ./
COPY src ./src
COPY --from=frontend /src/src/PalOps.Web/wwwroot ./src/PalOps.Web/wwwroot
RUN dotnet restore PalOpsWeb.slnx && dotnet publish src/PalOps.Web/PalOps.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://0.0.0.0:5178
EXPOSE 5178
ENTRYPOINT ["dotnet", "PalOps.Web.dll"]
