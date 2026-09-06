FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/BillingControl/BillingControl.csproj src/BillingControl/
RUN dotnet restore src/BillingControl/BillingControl.csproj
COPY src/BillingControl/ src/BillingControl/
RUN dotnet publish src/BillingControl/BillingControl.csproj -c Release --no-restore -o /app /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /keys && chown -R app:app /keys
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BillingControl.dll"]
