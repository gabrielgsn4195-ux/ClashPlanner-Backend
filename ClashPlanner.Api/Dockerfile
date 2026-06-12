# Imagen del backend ClashPlanner (ASP.NET Core 10). Build multi-etapa:
# compila con el SDK y ejecuta sobre la imagen ligera de runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restaura primero solo el csproj para aprovechar la caché de capas.
COPY ClashPlanner.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Escucha en el puerto 8080 (HTTP); la terminación TLS la hace el proxy inverso.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ClashPlanner.Api.dll"]
