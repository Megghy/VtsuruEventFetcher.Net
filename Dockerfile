# First stage: build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /src

# Copy the project files and build the release
COPY . .

RUN dotnet restore VtsuruEventFetcher.Net/VtsuruEventFetcher.Net.csproj
RUN dotnet publish VtsuruEventFetcher.Net/VtsuruEventFetcher.Net.csproj -c Release -o /app --no-restore

# Second stage: setup the runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app
EXPOSE 3000

# Copy the build output from the build stage
COPY --from=build-env /app ./

# Configure the container to run the application
ENTRYPOINT ["dotnet", "VtsuruEventFetcher.Net.dll"]