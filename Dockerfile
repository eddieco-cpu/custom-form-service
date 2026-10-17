FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /source
COPY global.json ./
COPY src/CustomFormService.Api/CustomFormService.Api.csproj src/CustomFormService.Api/
RUN dotnet restore src/CustomFormService.Api/CustomFormService.Api.csproj
COPY src/CustomFormService.Api/ src/CustomFormService.Api/
RUN dotnet publish src/CustomFormService.Api/CustomFormService.Api.csproj \
    --configuration Release --no-restore --output /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish/ ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://[::]:8080
EXPOSE 8080
USER app
CMD ["dotnet", "/app/CustomFormService.Api.dll"]
