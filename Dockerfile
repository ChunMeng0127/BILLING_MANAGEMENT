FROM node:22-alpine AS frontend
WORKDIR /frontend
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts --no-audit --no-fund
RUN mkdir -p /vendor/datatables \
    && cp node_modules/datatables.net/js/dataTables.min.js /vendor/datatables/ \
    && cp node_modules/datatables.net-dt/js/dataTables.dataTables.min.js /vendor/datatables/ \
    && cp node_modules/datatables.net-dt/css/dataTables.dataTables.min.css /vendor/datatables/ \
    && cp node_modules/datatables.net/License.txt /vendor/datatables/LICENSE.txt

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/BillingControl/BillingControl.csproj src/BillingControl/
RUN dotnet restore src/BillingControl/BillingControl.csproj
COPY src/BillingControl/ src/BillingControl/
COPY --from=frontend /vendor/datatables/ src/BillingControl/wwwroot/vendor/datatables/
RUN dotnet publish src/BillingControl/BillingControl.csproj -c Release --no-restore -o /app /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /keys && chown -R app:app /keys
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BillingControl.dll"]
